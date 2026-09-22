using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Velocity.Core;
using Velocity.Hardware.Internal;

namespace Velocity.Hardware;

/// <summary>
/// Находит установленные игры. Нужно для двух вещей:
///  • понять, на каком накопителе они лежат (игры на HDD — топовая находка диагностики);
///  • знать, к чему применять профили движков (docs/02-OPTIMIZATION-SPEC.md, раздел 2.11).
///
/// Читаем только манифесты лаунчеров — никаких обходов всего диска.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class GameLibraryScanner
{
    public static List<GameInfo> Scan()
    {
        var games = new List<GameInfo>();

        TryAdd(games, ScanSteam);
        TryAdd(games, ScanEpic);
        TryAdd(games, ScanXbox);

        // Подстраховка от дублей: одна и та же игра может попасться из двух источников
        // (например, установлена и в Steam, и в Game Pass).
        return [.. games
            .GroupBy(g => $"{g.Launcher}|{g.StoreId ?? g.InstallPath ?? g.Name}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(g => g.SizeGb)];
    }

    private static void TryAdd(List<GameInfo> games, Func<IEnumerable<GameInfo>> scanner)
    {
        try { games.AddRange(scanner()); }
        catch { /* лаунчер не установлен или манифесты битые — не повод ронять скан */ }
    }

    /// <summary>Проставляет каждой игре тип накопителя. Вызывается после скана дисков.</summary>
    public static void AttachDriveKinds(List<GameInfo> games, List<StorageInfo> storage)
    {
        foreach (var game in games)
        {
            if (game.DriveLetter is null) continue;
            var drive = storage.FirstOrDefault(s => s.DriveLetters.Contains(game.DriveLetter,
                StringComparer.OrdinalIgnoreCase));
            if (drive is not null) game.DriveKind = drive.Kind;
        }
    }

    // ─────────────────────────── Steam ───────────────────────────

    [GeneratedRegex(@"""path""\s*""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex SteamLibraryPath();

    [GeneratedRegex(@"""(appid|name|SizeOnDisk|installdir)""\s*""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex SteamManifestField();

    private static IEnumerable<GameInfo> ScanSteam()
    {
        var steamPath = Reg.Str(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath")
                     ?? Reg.Str(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
                     ?? Reg.Str(RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");

        if (steamPath is null || !Directory.Exists(steamPath)) yield break;

        foreach (var library in SteamLibraries(steamPath))
        {
            var appsDir = Path.Combine(library, "steamapps");
            if (!Directory.Exists(appsDir)) continue;

            string[] manifests;
            try { manifests = Directory.GetFiles(appsDir, "appmanifest_*.acf"); }
            catch { continue; }

            foreach (var manifest in manifests)
            {
                GameInfo? game = null;
                try { game = ParseSteamManifest(manifest, library); }
                catch { /* битый манифест */ }
                if (game is not null) yield return game;
            }
        }
    }

    private static IEnumerable<string> SteamLibraries(string steamPath)
    {
        // Дедупликация обязательна и должна идти через нормализованный путь:
        // в реестре Steam хранит путь через прямые слэши и в нижнем регистре
        // («c:/program files (x86)/steam»), а в libraryfolders.vdf — через обратные.
        // Наивное сравнение строк не совпадает, и библиотека обходится дважды,
        // из-за чего каждая игра попадает в отчёт по два раза.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Normalize(steamPath) is { } root && seen.Add(root))
            yield return root;

        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;

        string content;
        try { content = File.ReadAllText(vdf); }
        catch { yield break; }

        foreach (Match m in SteamLibraryPath().Matches(content))
        {
            // В VDF пути экранированы двойными слэшами.
            var path = Normalize(m.Groups[1].Value.Replace(@"\\", @"\"));
            if (path is not null && seen.Add(path)) yield return path;
        }
    }

    private static string? Normalize(string path)
    {
        try
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            return Directory.Exists(full) ? full : null;
        }
        catch { return null; }
    }

    private static GameInfo? ParseSteamManifest(string manifestPath, string libraryPath)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in SteamManifestField().Matches(File.ReadAllText(manifestPath)))
            fields[m.Groups[1].Value] = m.Groups[2].Value;

        if (!fields.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name)) return null;

        // Служебные записи Steam — не игры.
        if (name.StartsWith("Steamworks", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Proton", StringComparison.OrdinalIgnoreCase)) return null;

        var installDir = fields.GetValueOrDefault("installdir");
        var fullPath = installDir is not null
            ? Path.Combine(libraryPath, "steamapps", "common", installDir)
            : libraryPath;

        double sizeGb = 0;
        if (fields.TryGetValue("SizeOnDisk", out var raw) && double.TryParse(raw, out var bytes))
            sizeGb = Math.Round(bytes / 1024d / 1024d / 1024d, 1);

        return new GameInfo
        {
            Name = name,
            Launcher = GameLauncher.Steam,
            InstallPath = fullPath,
            DriveLetter = DriveOf(fullPath),
            SizeGb = sizeGb,
            StoreId = fields.GetValueOrDefault("appid")
        };
    }

    // ─────────────────────────── Epic Games ───────────────────────────

    private sealed class EpicManifest
    {
        public string? DisplayName { get; set; }
        public string? InstallLocation { get; set; }
        public long InstallSize { get; set; }
        public string? AppName { get; set; }
        public string? MainGameAppName { get; set; }
    }

    private static IEnumerable<GameInfo> ScanEpic()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(dir)) yield break;

        string[] files;
        try { files = Directory.GetFiles(dir, "*.item"); }
        catch { yield break; }

        foreach (var file in files)
        {
            EpicManifest? manifest = null;
            try
            {
                manifest = JsonSerializer.Deserialize<EpicManifest>(File.ReadAllText(file),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { /* битый манифест */ }

            if (manifest?.DisplayName is null || manifest.InstallLocation is null) continue;

            // DLC и дополнения ссылаются на основную игру — их пропускаем.
            if (manifest.MainGameAppName is { } main && main != manifest.AppName) continue;

            yield return new GameInfo
            {
                Name = manifest.DisplayName,
                Launcher = GameLauncher.EpicGames,
                InstallPath = manifest.InstallLocation,
                DriveLetter = DriveOf(manifest.InstallLocation),
                SizeGb = Math.Round(manifest.InstallSize / 1024d / 1024d / 1024d, 1),
                StoreId = manifest.AppName
            };
        }
    }

    // ─────────────────────────── Xbox / Game Pass ───────────────────────────

    private static IEnumerable<GameInfo> ScanXbox()
    {
        // Игры Game Pass ставятся в скрытую папку WindowsApps на каждом диске.
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;

            var dir = Path.Combine(drive.Name, "XboxGames");
            if (!Directory.Exists(dir)) continue;

            string[] entries;
            try { entries = Directory.GetDirectories(dir); }
            catch { continue; }

            foreach (var entry in entries)
            {
                yield return new GameInfo
                {
                    Name = Path.GetFileName(entry),
                    Launcher = GameLauncher.Xbox,
                    InstallPath = entry,
                    DriveLetter = DriveOf(entry)
                };
            }
        }
    }

    private static string? DriveOf(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return root?.TrimEnd('\\', '/') is { Length: 2 } letter ? letter.ToUpperInvariant() : null;
        }
        catch { return null; }
    }
}

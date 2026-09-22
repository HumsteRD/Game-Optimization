using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Velocity.App;

/// <summary>
/// Настройки самой программы. Лежат рядом с точками отката, в папке данных.
///
/// Сохранение никогда не бросает исключений: невозможность записать настройку —
/// не повод прерывать работу, пользователь просто потеряет одну галочку.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Velocity");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>Проверять обновления при запуске.</summary>
    public bool CheckUpdates { get; set; } = true;

    /// <summary>Скачивать и предлагать установку без отдельного вопроса о скачивании.</summary>
    public bool AutoDownloadUpdates { get; set; }

    /// <summary>Версия, о которой пользователь уже сказал «напомни позже».</summary>
    public string? SkippedVersion { get; set; }

    public DateTimeOffset? LastUpdateCheck { get; set; }

    /// <summary>Отправлять анонимную статистику применения настроек.</summary>
    public bool ShareTelemetry { get; set; }

    private static AppSettings? _current;
    public static AppSettings Current => _current ??= Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options)
                       ?? new AppSettings();
        }
        catch { /* повреждённый файл настроек — начинаем с умолчаний */ }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch { /* не смогли записать — не повод падать */ }
    }
}

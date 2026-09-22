using System.Runtime.InteropServices;
using Velocity.Core;

namespace Velocity.Scan.Cli;

/// <summary>
/// Консольный рендер отчёта. Палитра намеренно та же, что в docs/03-DESIGN.md —
/// CLI это первая витрина продукта, и выглядеть она должна как продукт, а не как дамп.
/// </summary>
internal static class Ui
{
    // Цвета вычисляются один раз при инициализации: если терминал не умеет ANSI
    // (старый conhost, перенаправленный вывод, файл), все они становятся пустыми строками
    // и отчёт остаётся читаемым обычным текстом.
    public static string Reset { get; private set; } = "";
    public static string Accent { get; private set; } = "";
    public static string Dim { get; private set; } = "";
    public static string White { get; private set; } = "";
    public static string Critical { get; private set; } = "";
    public static string Warning { get; private set; } = "";
    public static string Info { get; private set; } = "";
    public static string Success { get; private set; } = "";
    public static string Bold { get; private set; } = "";

    private static bool _colorEnabled;
    private static int Width { get; set; } = 78;

    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int nStdHandle);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr handle, uint mode);

    /// <summary>
    /// Включает ANSI в консоли Windows и подбирает ширину отчёта под окно.
    /// В старом conhost обработка escape-последовательностей выключена по умолчанию —
    /// без этого вызова пользователь увидит мусор вида «[38;2;0;229;180m» вместо цвета.
    /// </summary>
    public static void Init(bool forceNoColor)
    {
        try
        {
            Width = Math.Clamp(Console.WindowWidth - 2, 60, 100);
        }
        catch { /* вывод перенаправлен — оставляем значение по умолчанию */ }

        if (forceNoColor || Console.IsOutputRedirected) return;

        try
        {
            var handle = GetStdHandle(STD_OUTPUT_HANDLE);
            if (!GetConsoleMode(handle, out uint mode)) return;
            if ((mode & ENABLE_VIRTUAL_TERMINAL_PROCESSING) == 0 &&
                !SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING)) return;
        }
        catch { return; }

        _colorEnabled = true;
        Reset = "[0m";
        Accent = "[38;2;0;229;180m";
        Dim = "[38;2;120;130;150m";
        White = "[38;2;242;244;248m";
        Critical = "[38;2;244;63;94m";
        Warning = "[38;2;251;191;36m";
        Info = "[38;2;96;165;250m";
        Success = "[38;2;52;211;153m";
        Bold = "[1m";
    }

    public static void Line(string text, string? color = null) => Console.WriteLine($"{color ?? White}{text}{Reset}");

    /// <summary>Заголовок секции: «─── ИМЯ ────…» ровно по ширине отчёта.</summary>
    private static void Header(string title)
    {
        int dashes = Math.Max(3, Width - title.Length - 6);
        Line($"  ─── {title} {new string('─', dashes)}", Dim);
        Console.WriteLine();
    }

    public static void Banner()
    {
        Console.WriteLine();
        Line("  ╦  ╦╔═╗╦  ╔═╗╔═╗╦╔╦╗╦ ╦", Accent + Bold);
        Line("  ╚╗╔╝║╣ ║  ║ ║║  ║ ║ ╚╦╝", Accent + Bold);
        Line("   ╚╝ ╚═╝╩═╝╚═╝╚═╝╩ ╩  ╩ ", Accent + Bold);
        Line("  диагностика системы для игр", Dim);
        Console.WriteLine();
        Line("  Сканирование…", Dim);
    }

    public static void PrintReport(HardwareReport r)
    {
        // Затираем строку «Сканирование…» — только если терминал понимает управляющие последовательности.
        if (_colorEnabled) Console.Write("[1A[2K");

        PrintScore(r);
        PrintSpecs(r);
        PrintFindings(r);
        PrintFooter(r);
    }

    // ─────────────────────────── Замер мыши ───────────────────────────

    public static void MousePrompt()
    {
        if (_colorEnabled) Console.Write("[1A[2K");
        Console.WriteLine();
        Header("ЗАМЕР ЧАСТОТЫ ОПРОСА МЫШИ");
        Line("  Води мышью по кругу, не отрывая от коврика, пока идёт полоса.", White);
        Line("  Без движения мышь не шлёт отчёты — мерить будет нечего.", Dim);
        Console.WriteLine();
    }

    public static void MouseProgress(double fraction)
    {
        // Перерисовка через \r работает только в живой консоли. При перенаправлении
        // вывода каждая перерисовка превратилась бы в отдельную строку — молчим.
        if (Console.IsOutputRedirected) return;

        int filled = (int)Math.Round(fraction * 40);
        Console.Write($"\r  {Accent}{new string('█', filled)}{Dim}{new string('░', 40 - filled)}{Reset} " +
                      $"{Dim}{fraction * 100:0}%{Reset}  ");
    }

    public static void MouseResult(int normalizedHz, int rawHz, int samples, bool reliable)
    {
        if (!Console.IsOutputRedirected) Console.Write("\r" + new string(' ', 60) + "\r");

        if (normalizedHz == 0)
        {
            Line("  Не удалось измерить — мышь почти не двигалась.", Warning);
            Line("  Попробуй ещё раз и води активнее всё время замера.", Dim);
            Console.WriteLine();
            return;
        }

        var color = normalizedHz >= 500 ? Success : normalizedHz >= 250 ? Warning : Critical;
        Console.WriteLine($"  {color}{Bold}{normalizedHz} Гц{Reset}   " +
                          $"{Dim}(измерено {rawHz} Гц по {samples} отчётам){Reset}");

        if (!reliable)
            Line("  Разброс интервалов высокий — результат приблизительный. " +
                 "Так бывает при движении рывками или при загруженной системе.", Dim);

        Console.WriteLine();
    }

    public static void MouseHint()
    {
        Line("  Подсказка: запусти с ключом --mouse, чтобы измерить реальную частоту опроса мыши.", Dim);
        Console.WriteLine();
    }

    // ─────────────────────────── Score ───────────────────────────

    private static void PrintScore(HardwareReport r)
    {
        var color = ScoreColor(r.Score);
        int filled = (int)Math.Round(r.Score / 100.0 * 40);

        Console.WriteLine();
        Header("ОЦЕНКА СИСТЕМЫ");
        Console.WriteLine($"  {color}{Bold}{r.Score,3}{Reset}{Dim}/100{Reset}   " +
                          $"{color}{new string('█', filled)}{Dim}{new string('░', 40 - filled)}{Reset}   " +
                          $"{color}{Scoring.Label(r.Score)}{Reset}");
        Console.WriteLine();
    }

    private static string ScoreColor(int score) => score switch
    {
        >= 90 => Accent,
        >= 75 => Success,
        >= 55 => Warning,
        _ => Critical
    };

    // ─────────────────────────── Железо ───────────────────────────

    private static void PrintSpecs(HardwareReport r)
    {
        Header("КОНФИГУРАЦИЯ");

        Row("Система", $"{r.Machine.Manufacturer} {r.Machine.Model}".Trim() is { Length: > 1 } m ? m : "—");
        Row("Плата", $"{r.Machine.BoardManufacturer} {r.Machine.BoardProduct}".Trim());
        Row("Корпус", Translate(r.Machine.Chassis));
        Row("ОС", $"{r.Os.ProductName} {r.Os.DisplayVersion} (сборка {r.Os.FullBuild})");

        var cpu = r.Cpu;
        var cpuExtra = new List<string>();
        if (cpu.PhysicalCores > 0) cpuExtra.Add($"{cpu.PhysicalCores}C/{cpu.LogicalCores}T");
        if (cpu.IsHybrid) cpuExtra.Add($"{cpu.PerformanceCores}P + {cpu.EfficiencyCores}E");
        if (cpu.L3Domains > 1) cpuExtra.Add($"{cpu.L3Domains} L3-домена");
        if (cpu.IsAsymmetricCache) cpuExtra.Add("асимметричный кэш (X3D)");
        Row("Процессор", $"{cpu.Name}" + (cpuExtra.Count > 0 ? $"  {Dim}[{string.Join(", ", cpuExtra)}]{Reset}" : ""));

        foreach (var g in r.Gpus)
        {
            var vram = g.VramGb > 0 ? $"{g.VramGb} ГБ" : "—";
            var driver = g.VendorDriverVersion ?? g.DriverVersion;
            Row(g.IsIntegrated ? "Видео (встр.)" : "Видеокарта",
                $"{g.Name}  {Dim}[{vram}, драйвер {driver}]{Reset}");
        }

        var mem = r.Memory;
        var memNote = mem.Channels > 0 ? $", {mem.Channels}-канал" : "";
        Row("Память", $"{mem.TotalGb} ГБ {Pretty(mem.Kind)} @ {mem.ConfiguredSpeedMhz} МГц  " +
                      $"{Dim}[{mem.SlotsUsed}/{mem.SlotsTotal} слотов{memNote}]{Reset}");

        foreach (var s in r.Storage)
        {
            var letters = s.DriveLetters.Count > 0 ? string.Join(",", s.DriveLetters) : "—";
            // Для NVMe тип и шина совпадают — не дублируем «NVMe SSD NVMe».
            var kind = Pretty(s.Kind);
            var bus = s.BusType is { } b && !kind.Contains(b, StringComparison.OrdinalIgnoreCase) ? $" {b}" : "";
            Row(s.IsSystemDrive ? "Диск (система)" : "Диск",
                $"{s.Model}  {Dim}[{kind}{bus}, {s.SizeGb} ГБ, свободно {s.FreeGb} ГБ, {letters}]{Reset}");
        }

        foreach (var d in r.Displays)
        {
            var max = d.MaxRefreshAtCurrentResolution > 0 ? $", максимум {d.MaxRefreshAtCurrentResolution} Гц" : "";
            var hzColor = d.MaxRefreshAtCurrentResolution > d.RefreshHz + 1 ? Critical : White;
            Row(d.IsPrimary ? "Монитор (осн.)" : "Монитор",
                $"{d.FriendlyName}  {hzColor}{d.Width}×{d.Height} @ {d.RefreshHz} Гц{Reset}{Dim}{max}{Reset}");
        }

        foreach (var n in r.Network.Where(n => n.Connected && n.Kind != NetworkKind.Virtual))
            Row("Сеть", $"{n.ConnectionId ?? n.Name}  {Dim}[{Pretty(n.Kind)}, {n.LinkSpeedMbps} Мбит/с]{Reset}");

        foreach (var mouse in r.Input.Mice.Where(m => m.VendorId is not null))
            Row("Мышь", $"{mouse.Name}  {Dim}[{mouse.VendorName ?? mouse.VendorId}]{Reset}");

        if (r.Input.MeasuredPollingHz > 0)
            Row("Опрос мыши", $"{r.Input.MeasuredPollingHz} Гц {Dim}(измерено){Reset}");

        Row("Схема питания", r.Tuning.PowerPlanName ?? "—");
        Row("Таймер", r.Tuning.CurrentTimerResolutionMs is { } ms ? $"{ms:0.###} мс" : "—");

        if (r.Startup.ItemCount > 0)
            Row("Автозагрузка", Plural.Programs(r.Startup.ItemCount) +
                (r.Startup.HeavyItems.Count > 0
                    ? $"  {Dim}[тяжёлых: {string.Join(", ", r.Startup.HeavyItems)}]{Reset}"
                    : ""));

        Console.WriteLine();
        PrintGames(r);
    }

    private static void PrintGames(HardwareReport r)
    {
        if (r.Games.Count == 0) return;

        var byLauncher = r.Games.GroupBy(g => g.Launcher)
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToList();

        Header($"ИГРЫ: {r.Games.Count}");
        Line($"  {string.Join("   ", byLauncher)}", Dim);
        Console.WriteLine();

        // Показываем самые крупные — по ним и принимаются решения о переносе.
        foreach (var game in r.Games.Take(8))
        {
            var kindColor = game.DriveKind == StorageKind.Hdd ? Critical : Dim;
            var size = game.SizeGb > 0 ? $"{game.SizeGb} ГБ" : "—";
            var where = game.DriveLetter is { } d ? $"{d} {PrettyShort(game.DriveKind)}" : "—";
            Console.WriteLine($"  {White}{Truncate(game.Name, 38),-38}{Reset} " +
                              $"{Dim}{size,9}{Reset}  {kindColor}{where}{Reset}");
        }

        if (r.Games.Count > 8)
            Line($"  … и ещё {r.Games.Count - 8}", Dim);

        Console.WriteLine();
    }

    private static string PrettyShort(StorageKind kind) => kind switch
    {
        StorageKind.Hdd => "HDD",
        StorageKind.Ssd => "SSD",
        StorageKind.Nvme => "NVMe",
        _ => ""
    };

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..(max - 1)] + "…";

    private static void Row(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        Console.WriteLine($"  {Dim}{label,-16}{Reset} {value}");
    }

    private static string Pretty(MemoryKind kind) => kind switch
    {
        MemoryKind.Ddr3 => "DDR3",
        MemoryKind.Ddr4 => "DDR4",
        MemoryKind.Ddr5 => "DDR5",
        MemoryKind.Lpddr4 => "LPDDR4",
        MemoryKind.Lpddr5 => "LPDDR5",
        _ => "память"
    };

    private static string Pretty(StorageKind kind) => kind switch
    {
        StorageKind.Hdd => "HDD",
        StorageKind.Ssd => "SSD",
        StorageKind.Nvme => "NVMe SSD",
        _ => "накопитель"
    };

    private static string Pretty(NetworkKind kind) => kind switch
    {
        NetworkKind.Ethernet => "кабель",
        NetworkKind.WiFi => "Wi-Fi",
        NetworkKind.Bluetooth => "Bluetooth",
        NetworkKind.Virtual => "виртуальный",
        _ => "неизвестно"
    };

    private static string Translate(ChassisKind kind) => kind switch
    {
        ChassisKind.Desktop => "Настольный ПК",
        ChassisKind.Laptop => "Ноутбук",
        ChassisKind.AllInOne => "Моноблок",
        ChassisKind.Handheld => "Портативная консоль",
        ChassisKind.Server => "Сервер",
        _ => "Неизвестно"
    };

    // ─────────────────────────── Находки ───────────────────────────

    private static void PrintFindings(HardwareReport r)
    {
        var actionable = r.Findings.Where(f => f.Severity != FindingSeverity.Info).ToList();
        var informational = r.Findings.Where(f => f.Severity == FindingSeverity.Info).ToList();

        Header($"НАЙДЕНО ПРОБЛЕМ: {actionable.Count}");

        if (actionable.Count == 0)
            Line("  Критичных проблем не найдено — система настроена аккуратно.", Success);

        foreach (var f in actionable) PrintFinding(f);

        if (informational.Count > 0)
        {
            Header("К СВЕДЕНИЮ");
            foreach (var f in informational) PrintFinding(f);
        }
    }

    private static void PrintFinding(Finding f)
    {
        var (color, mark) = f.Severity switch
        {
            FindingSeverity.Critical => (Critical, "●"),
            FindingSeverity.Warning => (Warning, "●"),
            _ => (Info, "○")
        };

        Console.WriteLine($"  {color}{mark}{Reset} {Bold}{White}{f.Title}{Reset}");
        foreach (var line in Wrap(f.Detail, Width - 6))
            Console.WriteLine($"    {Dim}{line}{Reset}");

        if (f.Recommendation is { } rec)
        {
            var badge = f.AutoFixable ? $"{Accent}[авто]{Reset}" : $"{Dim}[вручную]{Reset}";
            Console.WriteLine($"    {Accent}→{Reset} {rec} {badge}");
        }

        if (f.ExpectedGain is { } gain)
            Console.WriteLine($"    {Dim}эффект: {gain}{Reset}");

        Console.WriteLine();
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new System.Text.StringBuilder();

        foreach (var word in words)
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }

        if (line.Length > 0) yield return line.ToString();
    }

    // ─────────────────────────── Подвал ───────────────────────────

    private static void PrintFooter(HardwareReport r)
    {
        Line("  " + new string('─', Width), Dim);

        var fixable = r.Findings.Count(f => f.AutoFixable);
        var manual = r.Findings.Count(f => f is { AutoFixable: false, Recommendation: not null });

        Console.WriteLine($"  {Dim}Скан занял {r.ScanDurationMs} мс · " +
                          $"автоматически исправимо: {Reset}{Accent}{fixable}{Reset}{Dim} · " +
                          $"требует твоих рук: {Reset}{White}{manual}{Reset}");

        if (r.ScanErrors.Count > 0)
        {
            Console.WriteLine();
            Line("  Часть данных собрать не удалось:", Warning);
            foreach (var e in r.ScanErrors) Line($"    · {e}", Dim);
        }

        Console.WriteLine();
    }
}

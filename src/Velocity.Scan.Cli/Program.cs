using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Velocity.Core;
using Velocity.Hardware;

namespace Velocity.Scan.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch { /* редкие консоли не дают сменить кодировку — не повод падать */ }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Velocity работает только на Windows.");
            return 2;
        }

        if (args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
        {
            PrintHelp();
            return 0;
        }

        bool jsonToStdout = args.Contains("--json");
        bool measureMouse = args.Contains("--mouse");
        bool pause = args.Contains("--pause");
        string? jsonPath = ArgValue(args, "--out");

        Ui.Init(forceNoColor: args.Contains("--no-color") || jsonToStdout);

        try
        {
            if (!jsonToStdout) Ui.Banner();

            var report = new HardwareScanner().Scan();

            if (measureMouse && !jsonToStdout)
                report.Input.MeasuredPollingHz = MeasureMouse();

            report.Findings = FindingsEngine.Analyze(report);
            report.Score = Scoring.Compute(report.Findings);

            if (jsonToStdout)
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
            else
                Ui.PrintReport(report);

            if (jsonPath is not null) SaveJson(report, jsonPath, quiet: jsonToStdout);
            if (!jsonToStdout && !measureMouse) Ui.MouseHint();

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Скан упал: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
        finally
        {
            // Двойной клик по exe закрывает окно мгновенно — на чужом ПК отчёт будет не прочитать.
            if (pause || (!jsonToStdout && !Console.IsOutputRedirected && LaunchedFromExplorer()))
            {
                Console.WriteLine();
                Console.Write("Нажми Enter, чтобы закрыть…");
                Console.ReadLine();
            }
        }
    }

    private static int MeasureMouse()
    {
        Ui.MousePrompt();

        var meter = new MousePollingMeter();
        var result = meter.Measure(TimeSpan.FromSeconds(6), Ui.MouseProgress);

        Ui.MouseResult(result.NormalizedHz, result.Hz, result.Samples, result.Reliable);
        return result.NormalizedHz;
    }

    private static void SaveJson(HardwareReport report, string path, bool quiet)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(full, JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(false));
            if (!quiet) Ui.Line($"  Отчёт сохранён: {full}", Ui.Dim);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Не удалось сохранить отчёт: {ex.Message}");
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processList, uint count);

    /// <summary>
    /// Определяет запуск двойным кликом: в этом случае к консоли привязан ровно один процесс — наш.
    /// Если exe запустили из уже открытого cmd/PowerShell, процессов будет минимум два,
    /// и ждать Enter не нужно — иначе программа зависнет в скриптах и CI.
    /// </summary>
    private static bool LaunchedFromExplorer()
    {
        try
        {
            var buffer = new uint[4];
            return GetConsoleProcessList(buffer, (uint)buffer.Length) == 1;
        }
        catch { return false; }
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void PrintHelp() => Console.WriteLine("""
        velocity-scan — диагностика системы для игр

          velocity-scan                      отчёт в консоль
          velocity-scan --mouse              + замер реальной частоты опроса мыши
          velocity-scan --out report.json    сохранить полный отчёт в JSON
          velocity-scan --json               отчёт в JSON в stdout (без оформления)
          velocity-scan --no-color           без цвета (для старых консолей и логов)
          velocity-scan --pause              ждать Enter перед закрытием окна
          velocity-scan --help               эта справка

        Права администратора не нужны. Программа ничего не меняет — только читает.
        """);
}

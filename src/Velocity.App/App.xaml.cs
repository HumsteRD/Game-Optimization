using System.Threading;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Velocity.App;

public partial class App : Application
{
    /// <summary>
    /// Имя должно совпадать с тем, что проверяет установщик (installer/velocity.iss):
    /// по нему он понимает, что программу нужно закрыть перед обновлением.
    /// Заодно не даём запустить второй экземпляр — две копии наперегонки
    /// применяли бы настройки и писали снимки поверх друг друга.
    /// </summary>
    private const string MutexName = "VelocityAppRunning";
    private static Mutex? _instanceLock;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceLock = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            FocusExistingWindow();
            Shutdown();
            return;
        }

        // Необработанное исключение в интерфейсе не должно выглядеть как «программа
        // просто исчезла». Показываем внятное окно и продолжаем работу, если можем.
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Report(ex, fatal: true);
        };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>Выводит вперёд уже запущенный экземпляр вместо молчаливого выхода.</summary>
    private static void FocusExistingWindow()
    {
        try
        {
            var current = System.Diagnostics.Process.GetCurrentProcess();
            var other = System.Diagnostics.Process.GetProcessesByName(current.ProcessName)
                .FirstOrDefault(p => p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero);

            if (other is null) return;

            ShowWindow(other.MainWindowHandle, 9); // SW_RESTORE — развернуть, если свёрнуто
            SetForegroundWindow(other.MainWindowHandle);
        }
        catch { /* не смогли переключиться — просто выходим */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceLock?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report(e.Exception, fatal: false);
        e.Handled = true;
    }

    private static void Report(Exception ex, bool fatal)
    {
        var text = $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";

        try
        {
            var log = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Velocity", "crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n\n");
        }
        catch { /* не смогли записать журнал — окно всё равно покажем */ }

        MessageBox.Show(
            fatal
                ? $"Произошла ошибка, работа будет прервана.\n\n{text}"
                : $"Произошла ошибка. Программа продолжит работу.\n\n{text}",
            "VELOCITY", MessageBoxButton.OK,
            fatal ? MessageBoxImage.Error : MessageBoxImage.Warning);
    }
}

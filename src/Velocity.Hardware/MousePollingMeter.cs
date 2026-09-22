using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Velocity.Hardware;

/// <summary>
/// Измеряет ФАКТИЧЕСКУЮ частоту опроса мыши.
///
/// Зачем это, а не кнопка «поставить 1000 Гц»: программно задать polling rate произвольной мыши
/// нельзя — это прошивка и вендорский софт. Зато можно честно измерить, что получается на деле,
/// и найти, где теряются герцы: USB 2.0-хаб, хаб монитора, энергосбережение порта, кривой драйвер.
/// Заявленные 1000 Гц и реальные 500 Гц — очень частая история.
///
/// Работает через Raw Input с флагом INPUTSINK: события приходят, даже когда окно неактивно,
/// поэтому консольное приложение может измерять, пока пользователь просто водит мышью.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MousePollingMeter
{
    private const int WM_INPUT = 0x00FF;
    private const int WM_QUIT = 0x0012;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RID_INPUT = 0x10000003;
    private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    private const ushort HID_USAGE_GENERIC_MOUSE = 0x02;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        [MarshalAs(UnmanagedType.FunctionPtr)] public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string? windowName,
        uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data,
        ref uint size, uint headerSize);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax, uint removeMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    public sealed record Result(int Hz, int NormalizedHz, int Samples, bool Reliable)
    {
        /// <summary>Ничего не намеряли — пользователь не двигал мышью либо это ноутбучный тачпад.</summary>
        public static readonly Result Empty = new(0, 0, 0, false);
    }

    private readonly List<double> _timestampsMs = [];
    private readonly Stopwatch _clock = new();

    // WndProc нельзя отдавать сборщику мусора, пока окно живо.
    private WndProcDelegate? _wndProc;

    /// <summary>
    /// Слушает мышь заданное время. Пользователь должен двигать мышью — без движения
    /// большинство мышей не шлют отчёты вообще, и мерить будет нечего.
    /// </summary>
    public Result Measure(TimeSpan duration, Action<double>? onProgress = null)
    {
        Result result = Result.Empty;

        // Message-only окно требует свой поток с насосом сообщений.
        var thread = new Thread(() => result = Pump(duration, onProgress)) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(duration + TimeSpan.FromSeconds(5));

        return result;
    }

    private Result Pump(TimeSpan duration, Action<double>? onProgress)
    {
        var className = "VelocityRawInput_" + Guid.NewGuid().ToString("N");
        _wndProc = WndProc;

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = GetModuleHandle(null),
            lpszClassName = className
        };

        if (RegisterClassEx(ref wc) == 0) return Result.Empty;

        var hwnd = CreateWindowEx(0, className, null, 0, 0, 0, 0, 0,
            HWND_MESSAGE, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero) return Result.Empty;

        try
        {
            var devices = new[]
            {
                new RAWINPUTDEVICE
                {
                    UsagePage = HID_USAGE_PAGE_GENERIC,
                    Usage = HID_USAGE_GENERIC_MOUSE,
                    Flags = RIDEV_INPUTSINK,   // получаем ввод, даже когда окно не в фокусе
                    Target = hwnd
                }
            };

            if (!RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
                return Result.Empty;

            _clock.Restart();
            double lastReport = 0;

            while (_clock.Elapsed < duration)
            {
                while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1 /* PM_REMOVE */))
                {
                    if (msg.message == WM_QUIT) break;
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                var elapsed = _clock.Elapsed.TotalMilliseconds;
                if (onProgress is not null && elapsed - lastReport > 100)
                {
                    lastReport = elapsed;
                    onProgress(Math.Min(1.0, elapsed / duration.TotalMilliseconds));
                }

                Thread.Sleep(1);
            }

            _clock.Stop();
            return Analyze();
        }
        finally
        {
            DestroyWindow(hwnd);
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT && HasMovement(lParam))
        {
            lock (_timestampsMs) _timestampsMs.Add(_clock.Elapsed.TotalMilliseconds);
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Считаем только отчёты с реальным перемещением: события кнопок и колеса
    /// приходят вне ритма опроса и завысили бы результат.
    /// </summary>
    private static bool HasMovement(IntPtr hRawInput)
    {
        uint size = 0;
        uint headerSize = (uint)(Marshal.SizeOf<uint>() * 2 + IntPtr.Size * 2); // RAWINPUTHEADER

        if (GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
            return false;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(hRawInput, RID_INPUT, buffer, ref size, headerSize) != size) return false;

            // RAWINPUTHEADER на x64 занимает 24 байта, дальше идёт RAWMOUSE,
            // в котором lLastX/lLastY лежат со смещением 12 и 16.
            int mouseOffset = (int)headerSize;
            if (size < mouseOffset + 20) return false;

            int lastX = Marshal.ReadInt32(buffer, mouseOffset + 12);
            int lastY = Marshal.ReadInt32(buffer, mouseOffset + 16);
            return lastX != 0 || lastY != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private Result Analyze()
    {
        double[] samples;
        lock (_timestampsMs) samples = [.. _timestampsMs];

        if (samples.Length < 30) return Result.Empty;

        // Интервалы между соседними отчётами. Паузы (мышь стояла) отбрасываем:
        // они не про частоту опроса, а про то, что пользователь отвлёкся.
        var intervals = new List<double>(samples.Length);
        for (int i = 1; i < samples.Length; i++)
        {
            var delta = samples[i] - samples[i - 1];
            if (delta is > 0 and < 20) intervals.Add(delta);
        }

        if (intervals.Count < 20) return Result.Empty;

        intervals.Sort();
        var median = intervals[intervals.Count / 2];
        if (median <= 0) return Result.Empty;

        int hz = (int)Math.Round(1000.0 / median);

        // Разброс интервалов показывает, стабилен ли опрос: у здоровой мыши
        // 10-й и 90-й перцентили почти совпадают с медианой.
        var p10 = intervals[(int)(intervals.Count * 0.10)];
        var p90 = intervals[(int)(intervals.Count * 0.90)];
        bool reliable = p90 > 0 && p90 / Math.Max(p10, 0.001) < 3.0 && intervals.Count >= 100;

        return new Result(hz, Normalize(hz), samples.Length, reliable);
    }

    /// <summary>Притягивает измеренное значение к ближайшей стандартной частоте.</summary>
    private static int Normalize(int hz)
    {
        int[] standard = [125, 250, 500, 1000, 2000, 4000, 8000];
        var closest = standard.MinBy(s => Math.Abs(s - hz));
        // Если промах больше 25% — не притягиваем, значение действительно нестандартное.
        return Math.Abs(closest - hz) <= closest * 0.25 ? closest : hz;
    }
}

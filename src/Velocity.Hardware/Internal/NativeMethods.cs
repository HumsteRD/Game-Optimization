using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Velocity.Hardware.Internal;

[SupportedOSPlatform("windows")]
internal static unsafe class NativeMethods
{
    // ─────────────────────────── Дисплеи ───────────────────────────

    public const int ENUM_CURRENT_SETTINGS = -1;
    public const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
    public const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;
    public const uint DISPLAY_DEVICE_MIRRORING_DRIVER = 0x00000008;
    /// <summary>Заставляет EnumDisplayDevices вернуть в DeviceID полный PnP-путь монитора.</summary>
    public const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    // ──────────────────── Топология процессора ────────────────────
    // GetLogicalProcessorInformationEx — единственный корректный способ узнать:
    //  • сколько P- и E-ядер (Intel 12th+/Core Ultra)  → EfficiencyClass
    //  • сколько L3-доменов и одинаковые ли они        → детект CCD и X3D у Ryzen

    public enum LOGICAL_PROCESSOR_RELATIONSHIP : uint
    {
        RelationProcessorCore = 0,
        RelationNumaNode = 1,
        RelationCache = 2,
        RelationProcessorPackage = 3,
        RelationGroup = 4,
        RelationAll = 0xffff
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetLogicalProcessorInformationEx(
        LOGICAL_PROCESSOR_RELATIONSHIP relationshipType, byte* buffer, ref uint returnedLength);

    public sealed record CoreTopology(int PhysicalCores, int PerformanceCores, int EfficiencyCores, bool IsHybrid);
    public sealed record CacheTopology(int L3Domains, bool Asymmetric, int LargestL3Kb, int SmallestL3Kb);

    /// <summary>Читает RelationProcessorCore и считает ядра по классам эффективности.</summary>
    public static CoreTopology? ReadCoreTopology()
    {
        var buf = QueryLpi(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore);
        if (buf is null) return null;

        var byClass = new Dictionary<byte, int>();
        fixed (byte* p = buf)
        {
            byte* cur = p, end = p + buf.Length;
            while (cur + 8 <= end)
            {
                uint size = *(uint*)(cur + 4);
                if (size < 8 || cur + size > end) break;
                // PROCESSOR_RELATIONSHIP: Flags@+8, EfficiencyClass@+9
                byte efficiencyClass = *(cur + 9);
                byClass[efficiencyClass] = byClass.GetValueOrDefault(efficiencyClass) + 1;
                cur += size;
            }
        }

        if (byClass.Count == 0) return null;
        int total = byClass.Values.Sum();
        if (byClass.Count == 1) return new CoreTopology(total, total, 0, false);

        // Больший EfficiencyClass = более производительное ядро.
        byte maxClass = byClass.Keys.Max();
        int pCores = byClass[maxClass];
        return new CoreTopology(total, pCores, total - pCores, true);
    }

    /// <summary>
    /// Читает RelationCache и анализирует L3. Разные размеры L3 между доменами —
    /// это Ryzen X3D (например, 7950X3D: 96 МБ на CCD0 и 32 МБ на CCD1).
    /// </summary>
    public static CacheTopology? ReadCacheTopology()
    {
        var buf = QueryLpi(LOGICAL_PROCESSOR_RELATIONSHIP.RelationCache);
        if (buf is null) return null;

        var l3Sizes = new List<int>();
        fixed (byte* p = buf)
        {
            byte* cur = p, end = p + buf.Length;
            while (cur + 8 <= end)
            {
                uint size = *(uint*)(cur + 4);
                if (size < 8 || cur + size > end) break;
                // CACHE_RELATIONSHIP: Level@+8, Assoc@+9, LineSize@+10, CacheSize@+12 (DWORD)
                byte level = *(cur + 8);
                if (level == 3)
                {
                    uint cacheBytes = *(uint*)(cur + 12);
                    l3Sizes.Add((int)(cacheBytes / 1024));
                }
                cur += size;
            }
        }

        if (l3Sizes.Count == 0) return null;
        return new CacheTopology(
            L3Domains: l3Sizes.Count,
            Asymmetric: l3Sizes.Distinct().Count() > 1,
            LargestL3Kb: l3Sizes.Max(),
            SmallestL3Kb: l3Sizes.Min());
    }

    private static byte[]? QueryLpi(LOGICAL_PROCESSOR_RELATIONSHIP rel)
    {
        uint len = 0;
        GetLogicalProcessorInformationEx(rel, null, ref len);
        if (len == 0) return null;

        var buf = new byte[len];
        fixed (byte* p = buf)
        {
            if (!GetLogicalProcessorInformationEx(rel, p, ref len)) return null;
        }
        return buf;
    }

    // ───────────────── Локализованные строки ресурсов ─────────────────

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string pszSource, System.Text.StringBuilder pszOutBuf,
        int cchOutBuf, IntPtr ppvReserved);

    /// <summary>
    /// Имена схем питания хранятся в реестре как ссылки на ресурс DLL. Резолвим их штатным API —
    /// так мы получаем строку на языке системы и не зависим от кодировки вывода powercfg.
    ///
    /// Осторожно: Windows пишет туда три компонента — «@C:\...\powrprof.dll,-13,High performance».
    /// SHLoadIndirectString понимает только первые два, третий (английский фолбэк) нужно отрезать.
    /// </summary>
    public static string? ResolveIndirectString(string value)
    {
        if (!value.StartsWith('@')) return value;

        var parts = value.Split(',');
        var resource = parts.Length >= 2 ? $"{parts[0]},{parts[1]}" : value;
        var fallback = parts.Length >= 3 ? string.Join(',', parts[2..]).Trim() : null;

        try
        {
            var buffer = new System.Text.StringBuilder(1024);
            if (SHLoadIndirectString(resource, buffer, buffer.Capacity, IntPtr.Zero) == 0 && buffer.Length > 0)
                return buffer.ToString();
        }
        catch { /* падаем на фолбэк ниже */ }

        return fallback;
    }

    // ───────────────────── Разрешение таймера ─────────────────────

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtQueryTimerResolution(out uint maximumTime, out uint minimumTime, out uint currentTime);

    /// <summary>Текущее разрешение системного таймера в миллисекундах (обычно 15.6 или 0.5).</summary>
    public static double? GetTimerResolutionMs()
    {
        try
        {
            if (NtQueryTimerResolution(out _, out _, out uint current) != 0) return null;
            return current / 10_000.0; // значение в 100-нс интервалах
        }
        catch { return null; }
    }
}

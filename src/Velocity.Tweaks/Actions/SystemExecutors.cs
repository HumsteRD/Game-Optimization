using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Velocity.Tweaks.Actions;

/// <summary>Схемы питания через powercfg. Прежняя схема запоминается целиком по GUID.</summary>
[SupportedOSPlatform("windows")]
public static class PowerExecutor
{
    private const string ActiveSchemeKey = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";

    public static StateRecord Capture(PowerCfgAction action)
    {
        string? value = action.Operation switch
        {
            "activate" => ReadActiveScheme(),
            "setvalue" => ReadSetting(action)?.ToString(),
            _ => null
        };

        return new StateRecord
        {
            Kind = "powercfg",
            Target = action.Operation == "activate"
                ? "active_scheme"
                : $"{action.SchemeGuid ?? "current"}/{action.SubGroupGuid}/{action.SettingGuid}",
            Value = value
        };
    }

    public static void Apply(PowerCfgAction action)
    {
        switch (action.Operation)
        {
            case "duplicate":
                // Схема Ultimate Performance скрыта, пока её не продублируешь.
                Run($"-duplicatescheme {action.SchemeGuid}");
                break;

            case "activate":
                Run($"-setactive {action.SchemeGuid}");
                break;

            case "setvalue":
                var scheme = action.SchemeGuid ?? "SCHEME_CURRENT";
                if (action.AcValue is { } ac)
                    Run($"-setacvalueindex {scheme} {action.SubGroupGuid} {action.SettingGuid} {ac}");
                if (action.DcValue is { } dc)
                    Run($"-setdcvalueindex {scheme} {action.SubGroupGuid} {action.SettingGuid} {dc}");
                // Без повторной активации изменения не вступают в силу.
                Run("-setactive SCHEME_CURRENT");
                break;
        }
    }

    public static void Restore(StateRecord record)
    {
        if (record.Value is null) return;

        if (record.Target == "active_scheme")
        {
            Run($"-setactive {record.Value}");
            return;
        }

        var parts = record.Target.Split('/');
        if (parts.Length != 3) return;

        var scheme = parts[0] == "current" ? "SCHEME_CURRENT" : parts[0];
        Run($"-setacvalueindex {scheme} {parts[1]} {parts[2]} {record.Value}");
        Run("-setactive SCHEME_CURRENT");
    }

    public static string? ReadActiveScheme()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(ActiveSchemeKey);
            return key?.GetValue("ActivePowerScheme")?.ToString();
        }
        catch { return null; }
    }

    public static int? ReadSetting(PowerCfgAction action)
    {
        // powercfg -query печатает и текущее значение — но парсить локализованный вывод хрупко.
        // Значения схем лежат в реестре, читаем оттуда.
        try
        {
            var scheme = action.SchemeGuid ?? ReadActiveScheme();
            if (scheme is null) return null;

            var path = $@"{ActiveSchemeKey}\{scheme}\{action.SubGroupGuid}\{action.SettingGuid}";
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(path);
            return key?.GetValue("ACSettingIndex") is { } v ? System.Convert.ToInt32(v) : null;
        }
        catch { return null; }
    }

    internal static void Run(string arguments)
    {
        var psi = new ProcessStartInfo("powercfg.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("powercfg не запустился");
        proc.WaitForExit(10_000);
    }
}

/// <summary>Режимы дисплея через ChangeDisplaySettingsEx.</summary>
[SupportedOSPlatform("windows")]
public static class DisplayExecutor
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint DISPLAY_DEVICE_ATTACHED = 0x01;
    private const uint DISPLAY_DEVICE_MIRRORING = 0x08;
    private const uint DM_BITSPERPEL = 0x00040000;
    private const uint DM_PELSWIDTH = 0x00080000;
    private const uint DM_PELSHEIGHT = 0x00100000;
    private const uint DM_DISPLAYFREQUENCY = 0x00400000;
    private const int CDS_UPDATEREGISTRY = 0x01;
    private const int DISP_CHANGE_SUCCESSFUL = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2;
        public uint dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DISPLAY_DEVICE info, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string? device, ref DEVMODE devMode, IntPtr hwnd,
        int flags, IntPtr param);

    public sealed record MonitorTarget(string Device, int Width, int Height, int RefreshHz, int BitsPerPixel);

    public static List<MonitorTarget> Enumerate()
    {
        var result = new List<MonitorTarget>();
        var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };

        for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++)
        {
            device.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
            if ((device.StateFlags & DISPLAY_DEVICE_ATTACHED) == 0) continue;
            if ((device.StateFlags & DISPLAY_DEVICE_MIRRORING) != 0) continue;

            var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(device.DeviceName, ENUM_CURRENT_SETTINGS, ref mode)) continue;

            result.Add(new MonitorTarget(device.DeviceName, (int)mode.dmPelsWidth, (int)mode.dmPelsHeight,
                (int)mode.dmDisplayFrequency, (int)mode.dmBitsPerPel));
        }

        return result;
    }

    /// <summary>Максимальная частота, доступная на текущем разрешении монитора.</summary>
    public static int MaxRefreshAt(string device, int width, int height)
    {
        int max = 0;
        var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };

        for (int i = 0; EnumDisplaySettings(device, i, ref mode); i++)
        {
            mode.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
            if (mode.dmPelsWidth == width && mode.dmPelsHeight == height && mode.dmDisplayFrequency > 1)
                max = Math.Max(max, (int)mode.dmDisplayFrequency);
        }

        return max;
    }

    public static List<StateRecord> Capture(DisplayModeAction action)
    {
        return [.. Targets(action).Select(t => new StateRecord
        {
            Kind = "display",
            Target = t.Device,
            Value = $"{t.Width}x{t.Height}@{t.RefreshHz}x{t.BitsPerPixel}"
        })];
    }

    public static void Apply(DisplayModeAction action)
    {
        foreach (var target in Targets(action))
        {
            int hz = action.Mode switch
            {
                "max_refresh" => MaxRefreshAt(target.Device, target.Width, target.Height),
                "refresh" => action.Value ?? target.RefreshHz,
                _ => target.RefreshHz
            };

            int bits = action.Mode == "bit_depth" ? action.Value ?? 32 : target.BitsPerPixel;
            if (hz <= 0) hz = target.RefreshHz;

            // Ничего не меняется — не трогаем экран лишний раз (это мигание и риск).
            if (hz == target.RefreshHz && bits == target.BitsPerPixel) continue;

            Change(target.Device, target.Width, target.Height, hz, bits);
        }
    }

    public static void Restore(StateRecord record)
    {
        // Формат: 1920x1080@144x32
        var parts = record.Value?.Split('x', '@');
        if (parts is not { Length: 4 }) return;

        if (int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h)
            && int.TryParse(parts[2], out var hz) && int.TryParse(parts[3], out var bits))
        {
            Change(record.Target, w, h, hz, bits);
        }
    }

    private static void Change(string device, int width, int height, int hz, int bits)
    {
        var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref mode)) return;

        mode.dmPelsWidth = (uint)width;
        mode.dmPelsHeight = (uint)height;
        mode.dmDisplayFrequency = (uint)hz;
        mode.dmBitsPerPel = (uint)bits;
        mode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY | DM_BITSPERPEL;

        var result = ChangeDisplaySettingsEx(device, ref mode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        if (result != DISP_CHANGE_SUCCESSFUL)
            throw new InvalidOperationException($"Смена режима {device} не удалась (код {result})");
    }

    private static IEnumerable<MonitorTarget> Targets(DisplayModeAction action)
        => action.Device is null ? Enumerate() : Enumerate().Where(t => t.Device == action.Device);
}

/// <summary>
/// Режим запуска служб. Служба НИКОГДА не удаляется — только переводится
/// в ручной запуск или отключается, чтобы изменение всегда можно было отменить.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ServiceExecutor
{
    private static string KeyOf(string name) => $@"SYSTEM\CurrentControlSet\Services\{name}";

    public static StateRecord Capture(ServiceAction action) => new()
    {
        Kind = "service",
        Target = action.Name,
        Value = ReadStart(action.Name)?.ToString()
    };

    public static void Apply(ServiceAction action)
    {
        int start = action.StartMode.ToLowerInvariant() switch
        {
            "auto" => 2,
            "manual" => 3,
            "disabled" => 4,
            _ => throw new ArgumentException($"Неизвестный режим запуска: {action.StartMode}")
        };
        WriteStart(action.Name, start);
    }

    public static void Restore(StateRecord record)
    {
        if (int.TryParse(record.Value, out var start)) WriteStart(record.Target, start);
    }

    private static int? ReadStart(string name)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(KeyOf(name));
            return key?.GetValue("Start") is { } v ? System.Convert.ToInt32(v) : null;
        }
        catch { return null; }
    }

    private static void WriteStart(string name, int start)
    {
        using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey(KeyOf(name), writable: true)
            ?? throw new InvalidOperationException($"Служба {name} не найдена");
        key.SetValue("Start", start, RegistryValueKind.DWord);
    }
}

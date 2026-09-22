using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Velocity.Hardware.Internal;

/// <summary>Чтение реестра без исключений. Отсутствующий ключ — норма, а не ошибка.</summary>
[SupportedOSPlatform("windows")]
internal static class Reg
{
    public static object? Raw(RegistryHive hive, string path, string name)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path);
            return key?.GetValue(name);
        }
        catch { return null; }
    }

    public static int? Int(RegistryHive hive, string path, string name)
        => Raw(hive, path, name) is { } v && int.TryParse(v.ToString(), out var i) ? i : null;

    public static uint? UInt(RegistryHive hive, string path, string name)
    {
        var v = Raw(hive, path, name);
        if (v is null) return null;
        // DWORD в реестре читается как int; 0xFFFFFFFF приезжает как -1.
        return v switch
        {
            int i => unchecked((uint)i),
            long l => unchecked((uint)l),
            _ => uint.TryParse(v.ToString(), out var u) ? u : null
        };
    }

    public static string? Str(RegistryHive hive, string path, string name)
        => Raw(hive, path, name)?.ToString();

    public static bool? Flag(RegistryHive hive, string path, string name)
        => Int(hive, path, name) is { } i ? i != 0 : null;

    public static string[] ValueNames(RegistryHive hive, string path)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path);
            return key?.GetValueNames() ?? [];
        }
        catch { return []; }
    }

    public static string[] SubKeys(RegistryHive hive, string path)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path);
            return key?.GetSubKeyNames() ?? [];
        }
        catch { return []; }
    }

    public const string HklmCurrentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    public const string HklmGraphicsDrivers = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    public const string HklmMultimediaProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    public const string HklmGamesTask = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
    public const string HklmDeviceGuard = @"SYSTEM\CurrentControlSet\Control\DeviceGuard";
    public const string HklmHvci = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
    public const string HklmSessionKernel = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
    public const string HklmSecureBoot = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";
    public const string HkcuMouse = @"Control Panel\Mouse";
    public const string HkcuGameConfigStore = @"System\GameConfigStore";
    public const string HkcuGameBar = @"Software\Microsoft\GameBar";
    public const string GpuClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
}

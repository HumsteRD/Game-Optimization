using System.Management;
using System.Runtime.Versioning;

namespace Velocity.Hardware.Internal;

/// <summary>
/// Обёртка над WMI. Главное правило: ни один запрос не имеет права уронить скан.
/// WMI на разном железе ведёт себя по-разному — половина классов может просто отсутствовать
/// (нет root\wmi, нет MSFT_PhysicalDisk на старых сборках, нет Win32_DeviceGuard на Home).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Wmi
{
    public static IEnumerable<ManagementObject> Query(string query, string scope = @"root\cimv2")
    {
        ManagementObjectCollection? results = null;
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query));
            results = searcher.Get();
            // Материализуем внутри try: перечисление тоже может кинуть.
            var list = new List<ManagementObject>();
            foreach (ManagementBaseObject o in results)
                if (o is ManagementObject mo) list.Add(mo);
            return list;
        }
        catch
        {
            return [];
        }
        finally
        {
            results?.Dispose();
        }
    }

    public static ManagementObject? QueryFirst(string query, string scope = @"root\cimv2")
        => Query(query, scope).FirstOrDefault();

    public static string? Str(this ManagementObject mo, string prop)
    {
        try { return mo[prop]?.ToString()?.Trim() is { Length: > 0 } s ? s : null; }
        catch { return null; }
    }

    public static int? Int(this ManagementObject mo, string prop)
    {
        try { return mo[prop] is null ? null : Convert.ToInt32(mo[prop]); }
        catch { return null; }
    }

    public static uint? UInt(this ManagementObject mo, string prop)
    {
        try { return mo[prop] is null ? null : Convert.ToUInt32(mo[prop]); }
        catch { return null; }
    }

    public static ulong? ULong(this ManagementObject mo, string prop)
    {
        try { return mo[prop] is null ? null : Convert.ToUInt64(mo[prop]); }
        catch { return null; }
    }

    public static bool? Bool(this ManagementObject mo, string prop)
    {
        try { return mo[prop] is null ? null : Convert.ToBoolean(mo[prop]); }
        catch { return null; }
    }

    public static ushort[]? UInt16Array(this ManagementObject mo, string prop)
    {
        try { return mo[prop] as ushort[]; }
        catch { return null; }
    }

    /// <summary>WMI отдаёт даты в формате CIM_DATETIME: 20240115000000.000000+180</summary>
    public static DateTime? CimDate(this ManagementObject mo, string prop)
    {
        try
        {
            var raw = mo[prop]?.ToString();
            return string.IsNullOrWhiteSpace(raw) ? null : ManagementDateTimeConverter.ToDateTime(raw);
        }
        catch { return null; }
    }
}

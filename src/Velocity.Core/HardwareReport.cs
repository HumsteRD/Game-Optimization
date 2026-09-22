using System.Text.Json.Serialization;

namespace Velocity.Core;

/// <summary>
/// Полный снимок конфигурации системы. Это фундамент всего продукта:
/// правила твиков (см. docs/02-OPTIMIZATION-SPEC.md) матчатся именно на эти поля.
/// </summary>
public sealed class HardwareReport
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;
    public long ScanDurationMs { get; set; }

    public MachineInfo Machine { get; set; } = new();
    public OsInfo Os { get; set; } = new();
    public CpuInfo Cpu { get; set; } = new();
    public MemoryInfo Memory { get; set; } = new();
    public List<GpuInfo> Gpus { get; set; } = [];
    public List<DisplayInfo> Displays { get; set; } = [];
    public List<StorageInfo> Storage { get; set; } = [];
    public List<NetworkInfo> Network { get; set; } = [];
    public InputInfo Input { get; set; } = new();
    public WindowsTuningState Tuning { get; set; } = new();
    public List<GameInfo> Games { get; set; } = [];
    public StartupInfo Startup { get; set; } = new();

    /// <summary>Ошибки отдельных сканеров. Один упавший сканер не должен ронять отчёт.</summary>
    public List<string> ScanErrors { get; set; } = [];

    public List<Finding> Findings { get; set; } = [];

    /// <summary>0..100. Считается из Findings, см. <see cref="Scoring"/>.</summary>
    public int Score { get; set; }
}

public sealed class MachineInfo
{
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public ChassisKind Chassis { get; set; } = ChassisKind.Unknown;
    public string? BoardManufacturer { get; set; }
    public string? BoardProduct { get; set; }
    public string? BiosVendor { get; set; }
    public string? BiosVersion { get; set; }
    public DateTime? BiosDate { get; set; }
    public bool? SecureBoot { get; set; }
    /// <summary>Стабильный компонент HWID (см. docs/04-LICENSING.md). В отчёте — уже хэш.</summary>
    public string? SystemUuidHash { get; set; }
}

public enum ChassisKind { Unknown, Desktop, Laptop, AllInOne, Server, Handheld }

public sealed class OsInfo
{
    public string? ProductName { get; set; }
    public string? DisplayVersion { get; set; }
    public int Build { get; set; }
    public int Ubr { get; set; }
    public string? Architecture { get; set; }
    public DateTime? InstallDate { get; set; }
    public TimeSpan Uptime { get; set; }
    public string? Locale { get; set; }

    [JsonIgnore] public bool IsWindows11 => Build >= 22000;
    public string FullBuild => $"{Build}.{Ubr}";
}

public sealed class CpuInfo
{
    public string? Name { get; set; }
    public string? Vendor { get; set; }
    public int PhysicalCores { get; set; }
    public int LogicalCores { get; set; }
    public int MaxClockMhz { get; set; }
    public int L2CacheKb { get; set; }
    public int L3CacheKb { get; set; }
    public string? Socket { get; set; }
    public bool? VirtualizationEnabled { get; set; }

    /// <summary>Гибридная архитектура (Intel 12th+ / Core Ultra): есть ядра разных классов эффективности.</summary>
    public bool IsHybrid { get; set; }
    public int PerformanceCores { get; set; }
    public int EfficiencyCores { get; set; }

    /// <summary>Количество CCD у Ryzen (эвристика по топологии L3-кэша).</summary>
    public int L3Domains { get; set; }
    /// <summary>Ryzen X3D: асимметричный L3 между CCD → нужен пиннинг игры на кэш-CCD.</summary>
    public bool IsAsymmetricCache { get; set; }

    [JsonIgnore] public bool IsAmd => Vendor?.Contains("AMD", StringComparison.OrdinalIgnoreCase) == true
                                    || Vendor?.Contains("AuthenticAMD", StringComparison.OrdinalIgnoreCase) == true;
    [JsonIgnore] public bool IsIntel => Vendor?.Contains("Intel", StringComparison.OrdinalIgnoreCase) == true
                                      || Vendor?.Contains("GenuineIntel", StringComparison.OrdinalIgnoreCase) == true;
}

public sealed class MemoryInfo
{
    public double TotalGb { get; set; }
    public double AvailableGb { get; set; }
    public int SlotsUsed { get; set; }
    public int SlotsTotal { get; set; }
    public MemoryKind Kind { get; set; } = MemoryKind.Unknown;
    /// <summary>Фактическая рабочая частота, МГц (эффективная, MT/s).</summary>
    public int ConfiguredSpeedMhz { get; set; }
    /// <summary>Максимальная частота, заявленная модулями по SPD.</summary>
    public int RatedSpeedMhz { get; set; }
    public int Channels { get; set; }
    public List<MemoryModule> Modules { get; set; } = [];

    public PagefileInfo Pagefile { get; set; } = new();
}

public enum MemoryKind { Unknown, Ddr3, Ddr4, Ddr5, Lpddr4, Lpddr5 }

public sealed class MemoryModule
{
    public string? Slot { get; set; }
    public string? BankLabel { get; set; }
    public double CapacityGb { get; set; }
    public int ConfiguredSpeedMhz { get; set; }
    public int RatedSpeedMhz { get; set; }
    public string? Manufacturer { get; set; }
    public string? PartNumber { get; set; }
}

public sealed class PagefileInfo
{
    public bool SystemManaged { get; set; }
    public bool Disabled { get; set; }
    public List<string> Locations { get; set; } = [];
    public double TotalSizeGb { get; set; }
}

public sealed class GpuInfo
{
    public string? Name { get; set; }
    public GpuVendor Vendor { get; set; } = GpuVendor.Unknown;
    public double VramGb { get; set; }
    public string? DriverVersion { get; set; }
    /// <summary>Версия драйвера в вендорском виде (для NVIDIA — 566.36, а не 32.0.15.6636).</summary>
    public string? VendorDriverVersion { get; set; }
    public DateTime? DriverDate { get; set; }
    public string? PnpDeviceId { get; set; }
    public bool IsIntegrated { get; set; }
}

public enum GpuVendor { Unknown, Nvidia, Amd, Intel, Microsoft }

public sealed class DisplayInfo
{
    public string? DeviceName { get; set; }
    public string? FriendlyName { get; set; }
    public bool IsPrimary { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshHz { get; set; }
    public int BitsPerPixel { get; set; }
    /// <summary>Максимальная частота, доступная на ТЕКУЩЕМ разрешении.</summary>
    public int MaxRefreshAtCurrentResolution { get; set; }
    /// <summary>Максимальная частота вообще (на любом разрешении).</summary>
    public int MaxRefreshAnyResolution { get; set; }
    public int MaxWidth { get; set; }
    public int MaxHeight { get; set; }
}

public sealed class StorageInfo
{
    public string? Model { get; set; }
    public StorageKind Kind { get; set; } = StorageKind.Unknown;
    public string? BusType { get; set; }
    public double SizeGb { get; set; }
    public double FreeGb { get; set; }
    public List<string> DriveLetters { get; set; } = [];
    public bool IsSystemDrive { get; set; }
    /// <summary>Флешки и внешние диски: показываем, но выводы по ним не делаем.</summary>
    public bool IsRemovable { get; set; }
    public string? HealthStatus { get; set; }

    [JsonIgnore] public double FreePercent => SizeGb > 0 ? FreeGb / SizeGb * 100 : 0;
}

public enum StorageKind { Unknown, Hdd, Ssd, Nvme }

public sealed class NetworkInfo
{
    public string? Name { get; set; }
    public string? ConnectionId { get; set; }
    public NetworkKind Kind { get; set; } = NetworkKind.Unknown;
    public long LinkSpeedMbps { get; set; }
    public bool Connected { get; set; }
    public string? InterfaceGuid { get; set; }
    public bool? NagleDisabled { get; set; }
}

public enum NetworkKind { Unknown, Ethernet, WiFi, Virtual, Bluetooth }

public sealed class InputInfo
{
    public List<HidDevice> Mice { get; set; } = [];
    public List<HidDevice> Keyboards { get; set; } = [];

    /// <summary>«Повышенная точность установки указателя» — акселерация мыши.</summary>
    public bool PointerPrecisionEnabled { get; set; }
    /// <summary>6/11 = 1:1 без масштабирования системой.</summary>
    public int MouseSensitivity { get; set; }
    public int MouseDataQueueSize { get; set; }
    public int KeyboardDataQueueSize { get; set; }
    /// <summary>Фактически измеренная частота опроса, Гц (Raw Input). 0 = не измерялась.</summary>
    public int MeasuredPollingHz { get; set; }
}

public sealed class HidDevice
{
    public string? Name { get; set; }
    public string? VendorId { get; set; }
    public string? ProductId { get; set; }
    public string? VendorName { get; set; }
    public bool OnUsbHub { get; set; }
    public bool PowerSavingEnabled { get; set; }
}

public sealed class GameInfo
{
    public required string Name { get; set; }
    public required GameLauncher Launcher { get; set; }
    public string? InstallPath { get; set; }
    public string? DriveLetter { get; set; }
    public double SizeGb { get; set; }
    /// <summary>Идентификатор в магазине (Steam AppID). Понадобится для профилей игр.</summary>
    public string? StoreId { get; set; }
    /// <summary>Тип накопителя, на котором лежит игра. Заполняется после скана дисков.</summary>
    public StorageKind DriveKind { get; set; } = StorageKind.Unknown;
}

public enum GameLauncher { Steam, EpicGames, Ea, Ubisoft, Battlenet, Riot, Xbox, Standalone }

public sealed class StartupInfo
{
    public int ItemCount { get; set; }
    public List<string> Items { get; set; } = [];
    /// <summary>Из автозагрузки — те, что известны как тяжёлые (RGB-софт, лаунчеры, оверлеи).</summary>
    public List<string> HeavyItems { get; set; } = [];
}

/// <summary>Состояние настроек Windows, влияющих на игры.</summary>
public sealed class WindowsTuningState
{
    public string? PowerPlanName { get; set; }
    public string? PowerPlanGuid { get; set; }
    public bool UltimatePerformanceAvailable { get; set; }

    public bool? GameDvrEnabled { get; set; }
    public bool? GameBarEnabled { get; set; }
    public bool? GameModeEnabled { get; set; }
    public bool? HardwareAcceleratedGpuScheduling { get; set; }

    public bool? VbsEnabled { get; set; }
    public bool? HvciEnabled { get; set; }

    public int? SystemResponsiveness { get; set; }
    public uint? NetworkThrottlingIndex { get; set; }
    public int? GamesGpuPriority { get; set; }
    public int? GamesPriority { get; set; }

    public bool? MemoryCompressionEnabled { get; set; }
    public bool? TransparencyEnabled { get; set; }
    public bool? GlobalTimerResolutionRequests { get; set; }
    public double? CurrentTimerResolutionMs { get; set; }

    /// <summary>Активные оверлеи (Steam/Discord/NVIDIA/RTSS/...). Каждый — хук в DXGI.</summary>
    public List<string> ActiveOverlays { get; set; } = [];
    /// <summary>Обнаруженные античиты. При активном античите твики не применяем.</summary>
    public List<string> ActiveAntiCheats { get; set; } = [];
    /// <summary>Следы вредных твиков из чужих .bat-паков (фича «Детокс»).</summary>
    public List<string> HarmfulTweaksDetected { get; set; } = [];
}

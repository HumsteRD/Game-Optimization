using System.Runtime.Versioning;

namespace Velocity.Hardware.Cleanup;

/// <summary>
/// Что именно считается мусором. Список закрытый и явный.
///
/// Здесь нет и не будет ни масок, ни «умного» поиска ненужных файлов: цена ошибки —
/// чужие данные. Каждый путь добавлен осознанно, с пониманием, что там лежит
/// и что сломается, если это удалить.
/// </summary>
[SupportedOSPlatform("windows")]
public static class CleanupCatalog
{
    public static List<CleanupTarget> All =>
    [
        new()
        {
            Id = "nvidia.installer_cache",
            Title = "Распакованные установщики драйверов NVIDIA",
            Description = "Установщик драйвера NVIDIA распаковывается в папку C:\\NVIDIA и после установки её не убирает. " +
                          "Каждое обновление драйвера добавляет туда ещё один полный комплект файлов. " +
                          "За год-два набегает несколько десятков гигабайт.",
            Consequence = "Ничего. Это временная папка распаковки, драйвер уже установлен и в ней не нуждается.",
            Kind = CleanupKind.Drivers,
            Safety = CleanupSafety.Safe,
            Paths =
            [
                "C:\\NVIDIA",
                "{programdata}\\NVIDIA Corporation\\Downloader",
                "{programdata}\\NVIDIA Corporation\\NetService",
                "{localappdata}\\NVIDIA Corporation\\NvTelemetry"
            ]
        },
        new()
        {
            Id = "amd.installer_cache",
            Title = "Распакованные установщики драйверов AMD",
            Description = "Пакеты драйверов AMD распаковываются в C:\\AMD и остаются там после установки.",
            Consequence = "Ничего. Драйвер уже установлен.",
            Kind = CleanupKind.Drivers,
            Safety = CleanupSafety.Safe,
            Paths = ["C:\\AMD"]
        },
        new()
        {
            Id = "windows.update_cache",
            Title = "Скачанные обновления Windows",
            Description = "Windows хранит установочные файлы обновлений после того, как они применены. " +
                          "Место освобождается сразу, на работу системы это не влияет.",
            Consequence = "Откатить уже установленное обновление станет сложнее. Сами обновления останутся установленными.",
            Kind = CleanupKind.WindowsUpdate,
            Safety = CleanupSafety.Regenerates,
            RequiresElevation = true,
            MinAgeDays = 7,
            Paths = ["{windows}\\SoftwareDistribution\\Download"]
        },
        new()
        {
            Id = "windows.delivery_optimization",
            Title = "Кэш раздачи обновлений другим компьютерам",
            Description = "Windows по умолчанию раздаёт скачанные обновления другим компьютерам и держит их копии у себя.",
            Consequence = "Ничего.",
            Kind = CleanupKind.WindowsUpdate,
            Safety = CleanupSafety.Safe,
            RequiresElevation = true,
            Paths = ["{windows}\\SoftwareDistribution\\DeliveryOptimization"]
        },
        new()
        {
            Id = "temp.user",
            Title = "Временные файлы",
            Description = "Программы складывают сюда промежуточные файлы и далеко не всегда убирают за собой. " +
                          "Файлы, созданные за последние сутки, не трогаем: они могут быть нужны работающим прямо сейчас программам.",
            Consequence = "Ничего.",
            Kind = CleanupKind.Temp,
            Safety = CleanupSafety.Safe,
            MinAgeDays = 1,
            Paths = ["{temp}", "{windows}\\Temp"]
        },
        new()
        {
            Id = "cache.shaders",
            Title = "Кэш шейдеров видеодрайвера",
            Description = "Драйвер хранит скомпилированные шейдеры, чтобы не компилировать их заново при каждом запуске игры. " +
                          "Нормальный размер — несколько гигабайт, но драйвер почти никогда не убирает записи от удалённых игр " +
                          "и от прошлых версий самого себя. За пару лет кэш дорастает до нескольких десятков гигабайт " +
                          "и становится самой жирной папкой в профиле пользователя.",
            Consequence = "Первый запуск каждой игры будет подтормаживать, пока шейдеры компилируются заново. Потом всё вернётся к норме.",
            Kind = CleanupKind.ShaderCache,
            Safety = CleanupSafety.Regenerates,
            Paths =
            [
                "{localappdata}\\NVIDIA\\DXCache",
                "{localappdata}\\NVIDIA\\GLCache",
                "{localappdata}\\NVIDIA\\ComputeCache",
                "{localappdata}\\NVIDIA\\NV_Cache",
                "{localappdata}\\AMD\\DxCache",
                "{localappdata}\\AMD\\GLCache",
                "{localappdata}\\D3DSCache",
                "{localappdata}\\Intel\\ShaderCache"
            ]
        },
        new()
        {
            Id = "dumps.crash",
            Title = "Отчёты о сбоях и дампы памяти",
            Description = "При падении программы или системы Windows сохраняет слепок памяти. " +
                          "Полный дамп системы весит столько же, сколько оперативная память.",
            Consequence = "Разобраться в причине давнего сбоя будет уже нельзя.",
            Kind = CleanupKind.CrashDumps,
            Safety = CleanupSafety.Safe,
            MinAgeDays = 7,
            Paths =
            [
                "{localappdata}\\CrashDumps",
                "{windows}\\Minidump",
                "{localappdata}\\Microsoft\\Windows\\WER"
            ]
        },
        new()
        {
            Id = "logs.windows",
            Title = "Журналы обслуживания Windows",
            Description = "Журналы установки компонентов и обновлений. Нужны только при разборе проблем с установкой.",
            Consequence = "Ничего для повседневной работы.",
            Kind = CleanupKind.Logs,
            Safety = CleanupSafety.Safe,
            RequiresElevation = true,
            MinAgeDays = 14,
            Paths = ["{windows}\\Logs\\CBS", "{windows}\\Logs\\DISM"]
        },
        new()
        {
            Id = "launchers.cache",
            Title = "Кэш игровых лаунчеров",
            Description = "Steam, Epic и Battle.net кэшируют картинки магазина и веб-страницы интерфейса. " +
                          "Сами игры и их файлы это не затрагивает.",
            Consequence = "Магазин и библиотека будут открываться чуть медленнее в первый раз.",
            Kind = CleanupKind.Launchers,
            Safety = CleanupSafety.Regenerates,
            Paths =
            [
                "{localappdata}\\Steam\\htmlcache",
                "{localappdata}\\EpicGamesLauncher\\Saved\\webcache",
                "{localappdata}\\EpicGamesLauncher\\Saved\\Logs",
                "{appdata}\\Battle.net\\Cache",
                "{localappdata}\\Battle.net\\Cache"
            ]
        },
        new()
        {
            Id = "windows.old",
            Title = "Предыдущая версия Windows",
            Description = "После крупного обновления Windows сохраняет старую систему целиком, чтобы можно было откатиться. " +
                          "Обычно это 15–30 гигабайт. Windows удаляет её сама через 10 дней, но не всегда.",
            Consequence = "Откатиться на предыдущую версию Windows станет невозможно. Если обновление прошло больше двух недель назад и всё работает, держать её смысла нет.",
            Kind = CleanupKind.WindowsUpdate,
            Safety = CleanupSafety.PointOfNoReturn,
            RequiresElevation = true,
            Paths = ["C:\\Windows.old"]
        }
    ];

    /// <summary>Раскрывает подстановки в пути категории.</summary>
    public static string Resolve(string template) => template
        .Replace("{temp}", Path.GetTempPath().TrimEnd('\\'))
        .Replace("{windows}", Environment.GetFolderPath(Environment.SpecialFolder.Windows))
        .Replace("{localappdata}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
        .Replace("{appdata}", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
        .Replace("{programdata}", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
        .Replace("{userprofile}", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
}

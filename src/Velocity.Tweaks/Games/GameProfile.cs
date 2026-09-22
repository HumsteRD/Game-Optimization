namespace Velocity.Tweaks.Games;

/// <summary>
/// Профиль конкретной игры. Здесь живут самые крупные приросты во всём продукте:
/// движковые настройки дают больше, чем любые системные твики вместе взятые
/// (docs/02-OPTIMIZATION-SPEC.md, раздел 2.11).
/// </summary>
public sealed class GameProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>AppID в Steam — по нему игра сопоставляется с найденной библиотекой.</summary>
    public string? SteamAppId { get; init; }
    /// <summary>Имена исполняемых файлов — запасной способ опознать игру.</summary>
    public List<string> Executables { get; init; } = [];
    public GameEngineKind Engine { get; init; } = GameEngineKind.Other;

    /// <summary>Рекомендуемые параметры запуска (Steam: свойства игры → параметры запуска).</summary>
    public string? LaunchOptions { get; init; }
    public string? LaunchOptionsWhy { get; init; }

    /// <summary>Правки конфигурационных файлов движка.</summary>
    public List<ConfigTweak> Config { get; init; } = [];

    /// <summary>Рекомендации по внутриигровым настройкам — их выставляет сам игрок.</summary>
    public List<GameSetting> Settings { get; init; } = [];

    /// <summary>Настройки драйвера NVIDIA для профиля именно этой игры.</summary>
    public List<DriverSetting> NvidiaSettings { get; init; } = [];

    /// <summary>Замечания, которые важнее любой настройки.</summary>
    public List<string> Notes { get; init; } = [];
}

public enum GameEngineKind { Source2, UnrealEngine4, UnrealEngine5, Unity, Frostbite, REEngine, IdTech, Other }

/// <summary>Одна правка конфигурационного файла.</summary>
public sealed class ConfigTweak
{
    /// <summary>
    /// Путь с подстановками: {game}, {documents}, {localappdata}, {appdata}, {steam_userdata}.
    /// </summary>
    public required string File { get; init; }
    public required ConfigFormat Format { get; init; }
    /// <summary>Секция для формата INI. Для остальных не используется.</summary>
    public string? Section { get; init; }
    public required Dictionary<string, string> Entries { get; init; }
    /// <summary>Создавать файл, если его нет (например, autoexec.cfg).</summary>
    public bool CreateIfMissing { get; init; }
    public string? Why { get; init; }
}

public enum ConfigFormat
{
    /// <summary>Классический INI с секциями — Unreal Engine, многие движки.</summary>
    Ini,
    /// <summary>Консольные команды Source: «fps_max 400» построчно.</summary>
    SourceCfg,
    /// <summary>key=value без секций.</summary>
    KeyValue
}

/// <summary>Рекомендация по внутриигровой настройке. Применяет её игрок сам.</summary>
public sealed class GameSetting
{
    public required string Name { get; init; }
    public required string Value { get; init; }
    public required string Why { get; init; }
    /// <summary>Условия железа, при которых совет уместен.</summary>
    public List<string> When { get; init; } = [];
    public SettingImpact Impact { get; init; } = SettingImpact.Medium;
}

public enum SettingImpact { Low, Medium, High }

public sealed class DriverSetting
{
    public required string Setting { get; init; }
    public required uint Value { get; init; }
    public required string Why { get; init; }
}

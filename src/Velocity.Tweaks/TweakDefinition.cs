using System.Text.Json.Serialization;

namespace Velocity.Tweaks;

/// <summary>
/// Твик — это ДАННЫЕ, а не код. Благодаря этому каталог обновляется с сервера
/// без переустановки программы (docs/02-OPTIMIZATION-SPEC.md, раздел 1).
/// </summary>
public sealed class TweakDefinition
{
    public required string Id { get; init; }
    public int Version { get; init; } = 1;
    public required TweakCategory Category { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }

    /// <summary>Что именно меняется — показываем пользователю до применения.</summary>
    public string? Effect { get; init; }
    /// <summary>Честная оценка выигрыша. Без «+300% FPS».</summary>
    public string? ExpectedGain { get; init; }

    public TweakRisk Risk { get; init; } = TweakRisk.Safe;
    public TweakConfidence Confidence { get; init; } = TweakConfidence.Documented;

    public bool RequiresReboot { get; init; }
    /// <summary>Применяем только при закрытой игре — иначе можно словить бан античита.</summary>
    public bool RequiresGameClosed { get; init; } = true;
    public bool RequiresElevation { get; init; }
    public bool AntiCheatSafe { get; init; } = true;

    /// <summary>Условия применимости: «gpu.vendor=nvidia», «os.build&gt;=19041».</summary>
    public List<string> Requires { get; init; } = [];
    /// <summary>Твики, которые нельзя включать одновременно с этим.</summary>
    public List<string> Conflicts { get; init; } = [];

    /// <summary>
    /// Как определить, применён ли твик. Если не задан, берётся первое действие
    /// из Apply — для реестра, питания и профиля драйвера этого достаточно,
    /// и не приходится дублировать одно и то же в каталоге.
    /// </summary>
    public TweakAction? Detect
    {
        get => _detect ?? Apply.FirstOrDefault(a => a is RegistryAction or PowerCfgAction or NvidiaProfileAction);
        init => _detect = value;
    }
    private readonly TweakAction? _detect;
    public List<TweakAction> Apply { get; init; } = [];
    /// <summary>Пусто = откат автоматический (восстановление снятого снимка).</summary>
    public List<TweakAction> Revert { get; init; } = [];

    /// <summary>В какие пресеты входит твик.</summary>
    public List<TweakPreset> Presets { get; init; } = [];

    public List<string> Sources { get; init; } = [];
}

public enum TweakCategory
{
    Display, Power, Cpu, Gpu, Windows, Security, Input, Memory, Storage, Network, Background, Game
}

public enum TweakRisk
{
    /// <summary>Обратимо, ничего не ломает, безопасность не трогает.</summary>
    Safe,
    /// <summary>Обратимо, но меняет поведение системы (сон, звук, экономия батареи).</summary>
    Moderate,
    /// <summary>Требует перезагрузки, меняет системные подсистемы.</summary>
    Advanced,
    /// <summary>Компромисс безопасности или стабильности. Только с явным согласием.</summary>
    Expert
}

public enum TweakConfidence
{
    /// <summary>Гипотеза. Только в расширенном режиме, помечена в интерфейсе.</summary>
    Experimental,
    /// <summary>Воспроизводимо по отзывам, но без строгих замеров.</summary>
    Community,
    /// <summary>Задокументировано вендором (Microsoft / NVIDIA / AMD / Intel).</summary>
    Documented,
    /// <summary>Замерено нами минимум на трёх конфигурациях.</summary>
    Measured
}

public enum TweakPreset { Safe, Gaming, Competitive, Maximum, Extreme, Laptop }

// ──────────────────────────── Действия ────────────────────────────

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RegistryAction), "registry")]
[JsonDerivedType(typeof(PowerCfgAction), "powercfg")]
[JsonDerivedType(typeof(DisplayModeAction), "display_mode")]
[JsonDerivedType(typeof(ServiceAction), "service")]
[JsonDerivedType(typeof(NvidiaProfileAction), "nvidia_profile")]
[JsonDerivedType(typeof(CommandAction), "command")]
public abstract class TweakAction
{
    /// <summary>Человекочитаемое описание действия для журнала.</summary>
    public abstract string Describe();
}

public sealed class RegistryAction : TweakAction
{
    /// <summary>HKLM, HKCU, HKCR, HKU.</summary>
    public required string Hive { get; init; }
    public required string Path { get; init; }
    public required string Name { get; init; }
    /// <summary>dword, qword, string, expand_string, multi_string, binary.</summary>
    public string Kind { get; init; } = "dword";
    /// <summary>Значение для записи. null означает удаление значения.</summary>
    public string? Value { get; init; }
    /// <summary>Ожидаемое значение при проверке состояния (detect).</summary>
    public string? Expected { get; init; }

    public override string Describe() =>
        Value is null ? $"{Hive}\\{Path} → удалить {Name}" : $"{Hive}\\{Path}\\{Name} = {Value}";
}

public sealed class PowerCfgAction : TweakAction
{
    /// <summary>duplicate | activate | setvalue.</summary>
    public required string Operation { get; init; }
    public string? SchemeGuid { get; init; }
    public string? SubGroupGuid { get; init; }
    public string? SettingGuid { get; init; }
    /// <summary>Значение для сети (AC).</summary>
    public int? AcValue { get; init; }
    /// <summary>Значение для батареи (DC). null — не трогаем.</summary>
    public int? DcValue { get; init; }

    public override string Describe() => Operation switch
    {
        "duplicate" => $"создать схему питания {SchemeGuid}",
        "activate" => $"активировать схему {SchemeGuid}",
        _ => $"питание: {SubGroupGuid}/{SettingGuid} = {AcValue}"
    };
}

public sealed class DisplayModeAction : TweakAction
{
    /// <summary>max_refresh — выставить максимальную доступную частоту. bit_depth — глубина цвета.</summary>
    public required string Mode { get; init; }
    /// <summary>Имя устройства (\\\\.\\DISPLAY1). null — все подключённые.</summary>
    public string? Device { get; init; }
    public int? Value { get; init; }

    public override string Describe() => Mode switch
    {
        "max_refresh" => "выставить максимальную частоту обновления",
        "bit_depth" => $"глубина цвета {Value} бит",
        _ => Mode
    };
}

public sealed class ServiceAction : TweakAction
{
    public required string Name { get; init; }
    /// <summary>auto | manual | disabled. Службы никогда не удаляем.</summary>
    public required string StartMode { get; init; }

    public override string Describe() => $"служба {Name} → {StartMode}";
}

public sealed class NvidiaProfileAction : TweakAction
{
    /// <summary>Имя настройки в драйверном профиле.</summary>
    public required string Setting { get; init; }
    public required uint Value { get; init; }
    /// <summary>null — глобальный профиль, иначе имя exe.</summary>
    public string? Application { get; init; }

    public override string Describe() =>
        $"NVIDIA: {Setting} = {Value}" + (Application is null ? " (глобально)" : $" ({Application})");
}

public sealed class CommandAction : TweakAction
{
    /// <summary>Только из белого списка: powercfg, bcdedit, netsh, dism, sc.</summary>
    public required string Executable { get; init; }
    public required string Arguments { get; init; }

    public override string Describe() => $"{Executable} {Arguments}";
}

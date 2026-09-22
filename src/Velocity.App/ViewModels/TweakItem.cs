using System.ComponentModel;
using System.Runtime.CompilerServices;
using Velocity.Tweaks;

namespace Velocity.App.ViewModels;

/// <summary>Как подаётся состояние настройки в списке.</summary>
public enum TweakBadge
{
    /// <summary>Можно применить — показываем переключатель.</summary>
    Actionable,
    /// <summary>Уже применено — переключатель не нужен, нужна отметка.</summary>
    Done,
    /// <summary>Делается руками в BIOS или в чужой программе — нужна инструкция.</summary>
    Manual
}

/// <summary>Твик вместе с его состоянием и отметкой выбора пользователем.</summary>
public sealed class TweakItem : INotifyPropertyChanged
{
    public required TweakDefinition Tweak { get; init; }

    private TweakState _state;
    public TweakState State
    {
        get => _state;
        set
        {
            _state = value;
            Notify();
            Notify(nameof(StateText));
            Notify(nameof(CanApply));
            Notify(nameof(Badge));
            Notify(nameof(ShowToggle));
            Notify(nameof(ShowDoneMark));
            Notify(nameof(ShowManualMark));
            Notify(nameof(DisabledReason));
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; Notify(); }
    }

    /// <summary>Раскрыта ли подробная справка по настройке.</summary>
    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; Notify(); }
    }

    public string Id => Tweak.Id;
    public string Title => Tweak.Title;
    public string Description => Tweak.Description;
    public string? Effect => Tweak.Effect;
    public string? ExpectedGain => Tweak.ExpectedGain;
    public TweakRisk Risk => Tweak.Risk;
    public bool RequiresReboot => Tweak.RequiresReboot;
    public bool RequiresElevation => Tweak.RequiresElevation;
    public List<string> ManualSteps => Tweak.ManualSteps;
    public bool HasManualSteps => Tweak.ManualSteps.Count > 0;

    /// <summary>Короткая строка под названием в свёрнутом виде.</summary>
    public string Headline => Tweak.ExpectedGain ?? Tweak.Effect ?? Tweak.Description;

    public TweakBadge Badge => Tweak.IsManual ? TweakBadge.Manual
        : State == TweakState.Applied ? TweakBadge.Done
        : TweakBadge.Actionable;

    public bool ShowToggle => Badge == TweakBadge.Actionable;
    public bool ShowDoneMark => Badge == TweakBadge.Done;
    public bool ShowManualMark => Badge == TweakBadge.Manual;

    public string StateText => State switch
    {
        TweakState.Applied => "применено",
        TweakState.NotApplied => "не применено",
        TweakState.NotApplicable => "не подходит",
        _ => "состояние неизвестно"
    };

    /// <summary>
    /// Почему переключатель недоступен. Пустой переключатель без объяснения —
    /// худшее, что может быть в интерфейсе: человек не понимает, сломано это или так задумано.
    /// </summary>
    public string? DisabledReason => Badge switch
    {
        TweakBadge.Done => "Уже применено. Вернуть прежнее значение можно в разделе «Точки отката».",
        TweakBadge.Manual => "Эту настройку программа изменить не может — она задаётся вне Windows. Ниже пошаговая инструкция.",
        _ => null
    };

    public bool CanApply => Badge == TweakBadge.Actionable;

    public string RiskText => Risk switch
    {
        TweakRisk.Safe => "Безопасно — обратимо, поведение системы не меняется",
        TweakRisk.Moderate => "Умеренно — обратимо, но меняет поведение системы",
        TweakRisk.Advanced => "Продвинуто — требует перезагрузки, затрагивает системные подсистемы",
        TweakRisk.Expert => "С риском — размен защиты системы на производительность",
        _ => ""
    };

    public string ConfidenceText => Tweak.Confidence switch
    {
        TweakConfidence.Measured => "Замерено на нескольких конфигурациях",
        TweakConfidence.Documented => "Задокументировано производителем",
        TweakConfidence.Community => "Воспроизводится по отзывам, строгих замеров нет",
        TweakConfidence.Experimental => "Гипотеза, эффект не подтверждён",
        _ => ""
    };

    public string CategoryName => Tweak.Category switch
    {
        TweakCategory.Display => "Монитор",
        TweakCategory.Power => "Питание",
        TweakCategory.Cpu => "Процессор",
        TweakCategory.Gpu => "Видеокарта",
        TweakCategory.Windows => "Windows",
        TweakCategory.Security => "Безопасность и производительность",
        TweakCategory.Input => "Мышь и клавиатура",
        TweakCategory.Memory => "Память",
        TweakCategory.Storage => "Накопители",
        TweakCategory.Network => "Сеть",
        TweakCategory.Background => "Фоновые программы",
        TweakCategory.Game => "Игры",
        _ => "Прочее"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

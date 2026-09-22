using System.ComponentModel;
using System.Runtime.CompilerServices;
using Velocity.Tweaks;

namespace Velocity.App.ViewModels;

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
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; Notify(); }
    }

    public string Id => Tweak.Id;
    public string Title => Tweak.Title;
    public string Description => Tweak.Description;
    public string? Effect => Tweak.Effect;
    public string? ExpectedGain => Tweak.ExpectedGain;
    public TweakRisk Risk => Tweak.Risk;
    public bool RequiresReboot => Tweak.RequiresReboot;

    public string StateText => State switch
    {
        TweakState.Applied => "применено",
        TweakState.NotApplied => "не применено",
        TweakState.NotApplicable => "не подходит",
        _ => "состояние неизвестно"
    };

    /// <summary>Применять имеет смысл только то, что ещё не применено и подходит системе.</summary>
    public bool CanApply => State is TweakState.NotApplied or TweakState.Unknown;

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
        TweakCategory.Background => "Фоновые процессы",
        TweakCategory.Game => "Игры",
        _ => "Прочее"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

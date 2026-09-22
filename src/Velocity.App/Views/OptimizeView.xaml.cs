using Velocity.Core;
using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Velocity.App.ViewModels;
using Velocity.Tweaks;

namespace Velocity.App.Views;

[SupportedOSPlatform("windows")]
public partial class OptimizeView : UserControl
{
    private readonly ObservableCollection<TweakItem> _items = [];
    private TweakPreset? _preset = TweakPreset.Safe;

    // XAML выставляет IsChecked у пресета по умолчанию ещё внутри InitializeComponent,
    // из-за чего Preset_Checked срабатывает до того, как созданы остальные элементы.
    // Флаг отсекает эти преждевременные вызовы.
    private bool _ready;

    private static readonly Dictionary<string, string> PresetHints = new()
    {
        ["Safe"] = "Только обратимые настройки, которые не меняют поведение системы и не требуют перезагрузки. Подходит всем без исключения.",
        ["Gaming"] = "Безопасный набор плюс настройки питания и фоновых процессов. Разумный выбор для большинства.",
        ["Competitive"] = "Упор на задержку ввода и ровный frametime: мышь, клавиатура, приоритеты планировщика, настройки драйвера.",
        ["Maximum"] = "Добавляются настройки, требующие перезагрузки. Эффект неоднозначный — после применения обязательно сравни ощущения.",
        ["Extreme"] = "Включает отключение защитных механизмов Windows ради кадров. Каждый такой пункт отмечен как рискованный — читай описание до применения.",
        ["All"] = "Полный каталог без фильтра. Здесь ты сам решаешь, что включать."
    };

    public OptimizeView()
    {
        InitializeComponent();

        var source = (CollectionViewSource)Resources["GroupedTweaks"];
        source.Source = _items;

        // Подписку держим на паре Loaded/Unloaded, а не на конструкторе:
        // представления кэшируются в MainWindow, и после первого ухода со вкладки
        // подписка из конструктора терялась навсегда.
        Loaded += (_, _) => AppState.Current.ReportChanged += Reload;
        Unloaded += (_, _) => AppState.Current.ReportChanged -= Reload;

        _ready = true;

        if (AppState.Current.HasReport) Reload();
        UpdateSelectionSummary();
    }

    private void Reload()
    {
        if (!_ready) return;

        Dispatcher.Invoke(() =>
        {
            var state = AppState.Current;
            if (state.Engine is null || state.Facts is null) return;

            var pool = _preset is { } preset
                ? state.Catalog.ByPreset(preset)
                : state.Catalog.All.ToList();

            // Твики, не подходящие системе, в список не попадают вовсе.
            // Показывать неактивные строки «не для вашего железа» — шум.
            var applicable = pool.Where(t => state.Facts.MatchesAll(t.Requires)).ToList();

            _items.Clear();
            foreach (var tweak in applicable.OrderBy(t => t.Category).ThenBy(t => t.Risk))
            {
                var status = state.Engine.GetStatus(tweak);
                var item = new TweakItem
                {
                    Tweak = tweak,
                    State = status.State,
                    // По умолчанию отмечаем то, что ещё не применено: пользователь
                    // нажимает «Применить» и получает ожидаемый результат без лишних кликов.
                    IsSelected = !tweak.IsManual && status.State is TweakState.NotApplied or TweakState.Unknown
                };
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(TweakItem.IsSelected)) UpdateSelectionSummary();
                };
                _items.Add(item);
            }

            var applied = _items.Count(i => i.State == TweakState.Applied);
            SubtitleText.Text = $"{Plural.With(_items.Count, "настройка", "настройки", "настроек")} " +
                                $"для этой системы · уже применено: {applied}";

            UpdateSelectionSummary();
        });
    }

    private void UpdateSelectionSummary()
    {
        if (!_ready) return;

        var selected = _items.Where(i => i.IsSelected && i.CanApply).ToList();
        var reboot = selected.Count(i => i.RequiresReboot);

        SelectionText.Text = selected.Count == 0
            ? "Ничего не выбрано"
            : $"Выбрано: {Plural.With(selected.Count, "настройка", "настройки", "настроек")}";

        SelectionHint.Text = reboot > 0
            ? $"{Plural.With(reboot, "настройка требует", "настройки требуют", "настроек требуют")} перезагрузки"
            : "Перед применением будет создана точка отката";

        ApplyButton.IsEnabled = selected.Count > 0;
    }

    private void Preset_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;

        _preset = tag == "All" ? null : Enum.Parse<TweakPreset>(tag);
        if (PresetHint is not null) PresetHint.Text = PresetHints.GetValueOrDefault(tag, "");
        if (AppState.Current.HasReport) Reload();
    }

    /// <summary>
    /// Клик по строке раскрывает справку. Переключатель при этом не трогаем:
    /// он обрабатывает своё нажатие сам и до сюда событие не доводит.
    /// </summary>
    private void Row_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TweakItem item })
            item.IsExpanded = !item.IsExpanded;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        // Кнопка работает как переключатель: второе нажатие снимает выбор.
        bool anyUnselected = _items.Any(i => i.CanApply && !i.IsSelected);
        foreach (var item in _items.Where(i => i.CanApply)) item.IsSelected = anyUnselected;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var state = AppState.Current;
        if (state.Engine is null) return;

        var selected = _items.Where(i => i.IsSelected && i.CanApply).Select(i => i.Tweak).ToList();
        if (selected.Count == 0) return;

        // Если среди выбранного есть настройки, требующие админа, а его нет —
        // предлагаем перезапуск ДО применения, иначе половина твиков молча пропустится.
        var needElevation = selected.Where(t => t.RequiresElevation).ToList();
        if (needElevation.Count > 0 && !Elevation.IsElevated)
        {
            var names = string.Join(Environment.NewLine,
                needElevation.Take(5).Select(t => "  • " + t.Title));
            if (needElevation.Count > 5)
                names += $"{Environment.NewLine}  и ещё {needElevation.Count - 5}";

            if (Elevation.RequestRestart(
                    $"Выбранным настройкам нужны права администратора:{Environment.NewLine}{Environment.NewLine}{names}"))
                return;

            var proceed = MessageBox.Show(
                $"Без прав администратора будет пропущено настроек: {needElevation.Count}." +
                $"{Environment.NewLine}{Environment.NewLine}Применить остальные?",
                "Часть настроек будет пропущена", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (proceed != MessageBoxResult.Yes) return;
        }

        var risky = selected.Where(t => t.Risk == TweakRisk.Expert).ToList();
        if (risky.Count > 0)
        {
            var names = string.Join("\n", risky.Select(t => "  • " + t.Title));
            var answer = MessageBox.Show(
                $"Среди выбранного есть настройки, которые снижают защиту системы:\n\n{names}\n\n" +
                "Они дают прирост кадров, но это осознанный размен. Откатить можно в любой момент.\n\n" +
                "Применить?",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes) return;
        }

        ApplyButton.IsEnabled = false;
        ApplyButton.Content = "Применяю…";

        var session = await Task.Run(() => state.Engine.Apply(selected,
            new TweakEngine.ApplyOptions { Reason = $"Набор «{_preset?.ToString() ?? "Все"}»" }));

        ApplyButton.Content = "Применить";

        if (session.AbortReason is { } abort)
        {
            MessageBox.Show(abort, "Применение отменено", MessageBoxButton.OK, MessageBoxImage.Warning);
            ApplyButton.IsEnabled = true;
            return;
        }

        ShowResult(session);

        // Именно пересканирование, а не пересчёт находок по старым данным:
        // состояние системы изменилось, и отчёт обязан это увидеть.
        await state.ScanAsync();
        Reload();
    }

    private static void ShowResult(TweakEngine.ApplySession session)
    {
        var applied = session.Results.Count(r => r.Outcome == ApplyOutcome.Applied);
        var failed = session.Results.Where(r => r.Outcome is ApplyOutcome.Failed or ApplyOutcome.VerifyFailed).ToList();
        var skipped = session.Results.Count(r => r.Outcome == ApplyOutcome.Skipped);

        var text = $"Применено: {applied}";
        if (skipped > 0) text += $"\nПропущено: {skipped}";

        if (failed.Count > 0)
        {
            text += $"\n\nНе удалось применить ({failed.Count}):\n" +
                    string.Join("\n", failed.Select(f => $"  • {f.Title} — {f.Message}"));
        }

        if (session.RebootRequired)
            text += "\n\nЧасть настроек вступит в силу после перезагрузки.";

        if (session.SnapshotId is { } id)
            text += $"\n\nТочка отката: {id}\nОткатить всё можно в разделе «Точки отката».";

        MessageBox.Show(text, "Готово", MessageBoxButton.OK,
            failed.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }
}

using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using Velocity.Core;
using Velocity.Tweaks;

namespace Velocity.App.Views;

public sealed class SnapshotCard
{
    public required string Id { get; init; }
    public required string When { get; init; }
    public required string Reason { get; init; }
    public required string Summary { get; init; }
    public required bool IsReverted { get; init; }
    public bool CanRevert => !IsReverted;
}

[SupportedOSPlatform("windows")]
public partial class RestoreView : UserControl
{
    private readonly SnapshotStore _store = new();

    public RestoreView()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        var snapshots = _store.List();

        var cards = snapshots.Select(s => new SnapshotCard
        {
            Id = s.Id,
            When = s.CreatedAt.ToString("d MMMM yyyy, HH:mm"),
            Reason = s.Reason,
            IsReverted = s.Reverted,
            Summary = Summarize(s)
        }).ToList();

        SnapshotList.ItemsSource = cards;
        EmptyState.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var active = cards.Count(c => c.CanRevert);
        SubtitleText.Text = cards.Count == 0
            ? "История изменений пуста"
            : $"{Plural.With(cards.Count, "точка", "точки", "точек")} · доступно для отката: {active}";
    }

    private static string Summarize(Snapshot snapshot)
    {
        if (snapshot.Changes.Count == 0) return "изменений не записано";

        var titles = snapshot.Changes.Select(c => c.TweakTitle).Take(3).ToList();
        var text = string.Join(", ", titles);

        if (snapshot.Changes.Count > titles.Count)
            text += $" и ещё {snapshot.Changes.Count - titles.Count}";

        var values = snapshot.Changes.Sum(c => c.Previous.Count);
        return $"{text}  ·  сохранено значений: {values}";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private async void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;

        var snapshot = _store.Load(id);
        if (snapshot is null) return;

        var answer = MessageBox.Show(
            $"Вернуть систему к состоянию до «{snapshot.Reason}»?\n\n" +
            $"Будет отменено изменений: {snapshot.Changes.Count}.\n" +
            "Часть настроек потребует перезагрузки, чтобы откат вступил в силу.",
            "Подтверждение отката", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes) return;

        var state = AppState.Current;
        // Откат должен работать даже до первого скана — создаём движок на пустых фактах.
        var engine = state.Engine ?? new TweakEngine(SystemFacts.From(new HardwareReport()), _store);

        var results = await Task.Run(() => engine.Revert(id));

        var failed = results.Where(r => r.Outcome == ApplyOutcome.Failed).ToList();
        var text = $"Откачено: {results.Count(r => r.Outcome == ApplyOutcome.Applied)}";
        if (failed.Count > 0)
            text += $"\n\nНе удалось ({failed.Count}):\n" +
                    string.Join("\n", failed.Select(f => $"  • {f.Title} — {f.Message}"));

        MessageBox.Show(text, "Откат завершён", MessageBoxButton.OK,
            failed.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

        Reload();
        state.RefreshFindings();
    }
}

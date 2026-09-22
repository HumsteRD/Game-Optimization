using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Velocity.Core;
using Velocity.Tweaks.Games;

namespace Velocity.App.Views;

public sealed class GameCard
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Location { get; init; }
    public string? LaunchOptions { get; init; }
    public string? LaunchOptionsWhy { get; init; }
    public List<SettingCard> Settings { get; init; } = [];
    public List<string> Notes { get; init; } = [];
}

public sealed class SettingCard
{
    public required string Name { get; init; }
    public required string Value { get; init; }
    public required string Why { get; init; }
    public required Brush ImpactBrush { get; init; }
}

[SupportedOSPlatform("windows")]
public partial class GamesView : UserControl
{
    private List<MatchedGame> _matched = [];

    public GamesView()
    {
        InitializeComponent();

        // Подписку держим на паре Loaded/Unloaded, а не на конструкторе:
        // представления кэшируются в MainWindow, и после первого ухода со вкладки
        // подписка из конструктора терялась навсегда.
        Loaded += (_, _) => AppState.Current.ReportChanged += Reload;
        Unloaded += (_, _) => AppState.Current.ReportChanged -= Reload;

        if (AppState.Current.HasReport) Reload();
    }

    private void Reload()
    {
        Dispatcher.Invoke(() =>
        {
            var state = AppState.Current;
            if (state.Report is null || state.Facts is null) return;

            _matched = state.GameProfiles.Match(state.Report.Games);

            var cards = _matched.Select(m => new GameCard
            {
                Id = m.Profile.Id,
                Name = m.Profile.Name,
                Location = Describe(m.Installed),
                LaunchOptions = GameProfileEngine.ResolveTokens(m.Profile.LaunchOptions, state.Facts),
                LaunchOptionsWhy = m.Profile.LaunchOptionsWhy,
                Notes = m.Profile.Notes,
                // Совет показываем только если он относится к этому железу.
                Settings = [.. m.Profile.Settings
                    .Where(s => s.When.Count == 0 || state.Facts.MatchesAll(s.When))
                    .Select(s => new SettingCard
                    {
                        Name = s.Name,
                        Value = s.Value,
                        Why = s.Why,
                        ImpactBrush = ImpactBrush(s.Impact)
                    })]
            }).ToList();

            GameList.ItemsSource = cards;
            EmptyState.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            SubtitleText.Text = cards.Count > 0
                ? $"Найдено {Plural.Games(cards.Count)} с готовым профилем " +
                  $"из {Plural.With(state.Report.Games.Count, "установленной", "установленных", "установленных")}"
                : $"Установленных игр найдено: {state.Report.Games.Count}";
        });
    }

    private static string Describe(GameInfo game)
    {
        var parts = new List<string>();
        if (game.SizeGb > 0) parts.Add($"{game.SizeGb} ГБ");
        if (game.DriveLetter is { } d)
        {
            var kind = game.DriveKind switch
            {
                StorageKind.Hdd => "жёсткий диск",
                StorageKind.Ssd => "SSD",
                StorageKind.Nvme => "NVMe",
                _ => ""
            };
            parts.Add($"диск {d} {kind}".TrimEnd());
        }
        parts.Add(game.Launcher.ToString());
        return string.Join("  ·  ", parts);
    }

    private Brush ImpactBrush(SettingImpact impact) => (Brush)FindResource(impact switch
    {
        SettingImpact.High => "Accent",
        SettingImpact.Medium => "Info",
        _ => "TextTertiary"
    });

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string text }) return;

        try
        {
            Clipboard.SetText(text);
            if (Window.GetWindow(this) is MainWindow main)
                main.SetStatus("Параметры запуска скопированы в буфер обмена");
        }
        catch
        {
            // Буфер обмена может быть занят другим приложением — это не повод падать.
            MessageBox.Show("Не удалось получить доступ к буферу обмена. Попробуй ещё раз.",
                "VELOCITY", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;

        var state = AppState.Current;
        var match = _matched.FirstOrDefault(m => m.Profile.Id == id);
        if (match is null || state.Facts is null) return;

        if (match.Profile.Config.Count == 0)
        {
            MessageBox.Show(
                "У этой игры нет правок конфигурационных файлов — всё, что можно улучшить, " +
                "выставляется в настройках самой игры. Список выше.",
                match.Profile.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var preview = state.GameProfiles.Apply(match, state.Facts, dryRun: true);
        var files = string.Join("\n", preview.Files.Select(f =>
            f.Error is null ? $"  • {f.File}" : $"  • {f.File} — {f.Error}"));

        var answer = MessageBox.Show(
            $"Будут изменены файлы настроек игры:\n\n{files}\n\n" +
            "Оригиналы сохраняются рядом — вернуть их можно в любой момент.\n\n" +
            "Игра должна быть закрыта, иначе она перезапишет конфиг при выходе.\n\nПродолжить?",
            match.Profile.Name, MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes) return;

        var result = state.GameProfiles.Apply(match, state.Facts);
        var changed = result.Files.Count(f => f.Error is null);
        var errors = result.Files.Where(f => f.Error is not null).ToList();

        var text = $"Изменено файлов: {changed}";
        if (result.DriverSettings.Count > 0)
            text += $"\nНастроек драйвера: {result.DriverSettings.Count}";
        if (errors.Count > 0)
            text += "\n\nНе удалось:\n" + string.Join("\n", errors.Select(f => $"  • {f.File} — {f.Error}"));

        MessageBox.Show(text, match.Profile.Name, MessageBoxButton.OK,
            errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }
}

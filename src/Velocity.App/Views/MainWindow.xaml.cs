using System.Reflection;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Velocity.Tweaks;

namespace Velocity.App.Views;

[SupportedOSPlatform("windows")]
public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _views = [];

    public MainWindow()
    {
        InitializeComponent();

        VersionText.Text = "версия " + (Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?.Split('+')[0] ?? "0.2.0");

        AdminBadge.Visibility = TweakEngine.IsElevated() ? Visibility.Visible : Visibility.Collapsed;

        Show("Dashboard");
        Loaded += async (_, _) =>
        {
            SetStatus("Сканирование системы…");
            await AppState.Current.ScanAsync();
            SetStatus($"Скан занял {AppState.Current.Report?.ScanDurationMs} мс");
        };
    }

    public void SetStatus(string text) => StatusText.Text = text;

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // Обработчик срабатывает и при первичной установке IsChecked в разметке,
        // когда контейнера содержимого ещё нет.
        if (ContentHost is null) return;
        if (sender is RadioButton { Tag: string tag }) Show(tag);
    }

    private void Show(string key)
    {
        if (!_views.TryGetValue(key, out var view))
        {
            view = key switch
            {
                "Dashboard" => new DashboardView(),
                "Optimize" => new OptimizeView(),
                "Games" => new GamesView(),
                "Restore" => new RestoreView(),
                "Settings" => new SettingsView(),
                _ => new DashboardView()
            };
            _views[key] = view;
        }

        ContentHost.Content = view;
    }

    // ─────────────────────── Управление окном ───────────────────────

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        // Символы «развернуть» и «восстановить» в шрифте Segoe Fluent Icons разные.
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "" : "";
    }
}

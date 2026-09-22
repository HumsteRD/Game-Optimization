using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using Velocity.Tweaks;
using Velocity.Tweaks.Actions;

namespace Velocity.App.Views;

[SupportedOSPlatform("windows")]
public partial class SettingsView : UserControl
{
    private static string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Velocity");

    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => Render();
    }

    private void Render()
    {
        var state = AppState.Current;

        AboutList.ItemsSource = new List<SpecRow>
        {
            new("Версия", Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
                ?? "0.2.0"),
            new("Права администратора", TweakEngine.IsElevated() ? "есть" : "нет"),
            new("Драйвер NVIDIA (NVAPI)", NvidiaExecutor.IsAvailable ? "доступен" : "не обнаружен"),
            new("Активные античиты", TweakEngine.DetectActiveAntiCheats() is { Count: > 0 } ac
                ? string.Join(", ", ac)
                : "не обнаружены")
        };

        DataList.ItemsSource = new List<SpecRow>
        {
            new("Папка данных", DataFolder),
            new("Точек отката", new SnapshotStore().List().Count.ToString())
        };

        CatalogList.ItemsSource = new List<SpecRow>
        {
            new("Настроек в каталоге", state.Catalog.All.Count.ToString()),
            new("Подходит этой системе", state.Facts is null
                ? "—"
                : state.Catalog.Applicable(state.Facts).Count.ToString()),
            new("Профилей игр", state.GameProfiles.Profiles.Count.ToString())
        };

        ErrorList.ItemsSource = state.Catalog.LoadErrors.Concat(state.GameProfiles.LoadErrors).ToList();
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось открыть папку: {ex.Message}", "VELOCITY",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        var report = AppState.Current.Report;
        if (report is null)
        {
            MessageBox.Show("Отчёт ещё не готов — дождись окончания скана.", "VELOCITY",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"velocity-отчёт-{DateTime.Now:yyyy-MM-dd-HHmm}.json",
            Filter = "Файл JSON|*.json",
            Title = "Сохранить отчёт о системе"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Converters = { new JsonStringEnumConverter() },
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(report, options));

            if (Window.GetWindow(this) is MainWindow main) main.SetStatus("Отчёт сохранён");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось сохранить: {ex.Message}", "VELOCITY",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

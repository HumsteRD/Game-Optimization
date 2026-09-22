using System.Runtime.Versioning;
using System.Windows;

namespace Velocity.App;

/// <summary>
/// Диалог обновления: предложить, скачать, поставить.
///
/// Вынесен отдельно, потому что вызывается из двух мест — автоматической проверки
/// при запуске и кнопки в настройках, — и логика подтверждения должна быть общей.
/// </summary>
[SupportedOSPlatform("windows")]
public static class UpdateFlow
{
    private static bool _busy;

    /// <summary>Проверка при запуске. Молчит, если обновлений нет или проверка выключена.</summary>
    public static async Task CheckOnStartupAsync(Window? owner)
    {
        var settings = AppSettings.Current;
        if (!settings.CheckUpdates) return;

        // Чаще раза в сутки дёргать GitHub незачем.
        if (settings.LastUpdateCheck is { } last && DateTimeOffset.Now - last < TimeSpan.FromDays(1))
            return;

        var update = await Updater.CheckAsync();

        settings.LastUpdateCheck = DateTimeOffset.Now;
        settings.Save();

        if (update is null) return;

        // Версию, от которой пользователь уже отказался, второй раз не навязываем.
        if (settings.SkippedVersion == update.Version) return;

        await OfferAsync(update, owner);
    }

    public static async Task OfferAsync(UpdateInfo update, Window? owner)
    {
        if (_busy) return;

        var sizeMb = Math.Round(update.SizeBytes / 1024d / 1024d, 1);
        var notes = Summarize(update.Notes);

        var answer = MessageBox.Show(
            $"Доступна версия {update.Version}. Установлена {Updater.CurrentVersion}." +
            $"{Environment.NewLine}{Environment.NewLine}{notes}" +
            $"{Environment.NewLine}{Environment.NewLine}Размер загрузки: {sizeMb} МБ." +
            $"{Environment.NewLine}{Environment.NewLine}Скачать и установить?",
            "Доступно обновление", MessageBoxButton.YesNo, MessageBoxImage.Information);

        if (answer != MessageBoxResult.Yes)
        {
            // Запоминаем отказ, чтобы не спрашивать про эту же версию при каждом запуске.
            AppSettings.Current.SkippedVersion = update.Version;
            AppSettings.Current.Save();
            return;
        }

        await DownloadAndInstallAsync(update, owner);
    }

    private static async Task DownloadAndInstallAsync(UpdateInfo update, Window? owner)
    {
        _busy = true;
        var main = owner as Views.MainWindow;

        try
        {
            var progress = new Progress<double>(fraction =>
                main?.SetStatus($"Загрузка обновления: {fraction * 100:0}%"));

            main?.SetStatus("Загрузка обновления…");
            var installer = await Updater.DownloadAsync(update, progress);

            main?.SetStatus("Обновление загружено");

            var answer = MessageBox.Show(
                $"Версия {update.Version} загружена." +
                $"{Environment.NewLine}{Environment.NewLine}" +
                "Программа закроется, установится обновление и запустится снова. " +
                "Применённые настройки и точки отката сохранятся." +
                $"{Environment.NewLine}{Environment.NewLine}Установить сейчас?",
                "Готово к установке", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer == MessageBoxResult.Yes)
            {
                Updater.Install(installer);
                return;
            }

            main?.SetStatus("Обновление загружено и ждёт установки");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось загрузить обновление: {ex.Message}" +
                $"{Environment.NewLine}{Environment.NewLine}" +
                "Скачанная часть сохранена — следующая попытка продолжит с того же места. " +
                $"Можно скачать установщик вручную: {update.PageUrl}",
                "Ошибка загрузки", MessageBoxButton.OK, MessageBoxImage.Warning);

            main?.SetStatus("Загрузка обновления не удалась");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Достаёт из описания релиза несколько первых осмысленных строк.
    /// Целиком оно длинное и в окне сообщения нечитаемо.
    /// </summary>
    private static string Summarize(string notes)
    {
        var lines = notes
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#') && !l.StartsWith("---") && !l.StartsWith('['))
            .Take(4)
            .ToList();

        return lines.Count > 0
            ? string.Join(Environment.NewLine, lines)
            : "Описание изменений доступно на странице релиза.";
    }
}

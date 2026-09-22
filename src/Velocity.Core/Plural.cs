namespace Velocity.Core;

/// <summary>
/// Русские числительные. Мелочь, но «5 игр(ы)» и «23 программ» мгновенно выдают
/// самописную поделку — а мы продаём продукт, а не скрипт.
/// </summary>
public static class Plural
{
    /// <summary>
    /// Выбирает форму слова по числу: 1 игра, 2 игры, 5 игр.
    /// </summary>
    public static string Of(int count, string one, string few, string many)
    {
        int mod100 = Math.Abs(count) % 100;
        int mod10 = mod100 % 10;

        // 11–14 всегда идут по форме «много»: одиннадцать игр, двенадцать игр.
        if (mod100 is >= 11 and <= 14) return many;

        return mod10 switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many
        };
    }

    /// <summary>Число вместе с согласованным словом: «23 программы».</summary>
    public static string With(int count, string one, string few, string many)
        => $"{count} {Of(count, one, few, many)}";

    public static string Games(int count) => With(count, "игра", "игры", "игр");
    public static string Programs(int count) => With(count, "программа", "программы", "программ");
    public static string Problems(int count) => With(count, "проблема", "проблемы", "проблем");
    public static string Overlays(int count) => With(count, "оверлей", "оверлея", "оверлеев");
    public static string Months(int count) => With(count, "месяц", "месяца", "месяцев");
}

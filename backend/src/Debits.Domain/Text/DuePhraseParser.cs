using System.Text.RegularExpressions;

namespace Debits.Domain.Text;

/// <summary>
/// Extracts a due date from a phrase in uz-Latn / uz-Cyrl / ru:
/// "3 kunga", "bir haftaga", "oy oxirigacha", "ertaga", "на неделю", "до конца месяца", "через 10 дней".
/// </summary>
public static partial class DuePhraseParser
{
    public sealed record DueMatch(DateOnly DueDate, string MatchedText);

    private const string Num = @"(?<n>\d+|bir|ikki|uch|tort|besh|olti|yetti|sakkiz|toqqiz|on|on\s+(?:bir|ikki|uch|tort|besh)|yigirma|ottiz|qirq|ellik|odin|odnu|odna|dva|dve|tri|chetire|pyat|shest|sem|vosem|devyat|desyat|odinnadtsat|dvenadtsat|pyatnadtsat|dvadtsat|tridtsat|sorok|polmesyatsa)";

    [GeneratedRegex(@"\b(?:oy(?:ning)?\s+oxiri(?:ga|gacha|da)?|oyning\s+oxirigacha|(?:do\s+)?kontsa\s+mesyatsa|v\s+kontse\s+mesyatsa|k\s+kontsu\s+mesyatsa)\b", RegexOptions.CultureInvariant)]
    private static partial Regex EndOfMonth();

    [GeneratedRegex(@"\b(?:hafta\s+oxiri(?:ga|gacha|da)?|(?:do\s+)?kontsa\s+nedeli|v\s+kontse\s+nedeli)\b", RegexOptions.CultureInvariant)]
    private static partial Regex EndOfWeek();

    [GeneratedRegex(@"\b(?:indin(?:ga)?|poslezavtra)\b", RegexOptions.CultureInvariant)]
    private static partial Regex DayAfterTomorrow();

    [GeneratedRegex(@"\b(?:ertaga|ertagacha|zavtra)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Tomorrow();

    [GeneratedRegex($@"\b{Num}\s*(?:-\s*)?(?:kun(?:ga|gacha|dan|lik|da)?|kunlik|den|dnya|dney|dnej|dn)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Days();

    [GeneratedRegex($@"\b(?:{Num}\s*(?:-\s*)?)?(?:hafta(?:ga|gacha|dan|lik|da)?|nedel(?:yu|ya|i|ey|e)?)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Weeks();

    [GeneratedRegex($@"\b(?:{Num}\s*(?:-\s*)?)?(?:oy(?:ga|gacha|dan|lik|da)?|mesyats(?:a|ev|u)?)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Months();

    public static DueMatch? Parse(string text, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var norm = SearchKey.NormalizeText(text);

        if (EndOfMonth().Match(norm) is { Success: true } eom)
        {
            var end = EndOfMonthDate(today);
            if (end <= today)
            {
                end = EndOfMonthDate(today.AddMonths(1));
            }

            return new DueMatch(end, eom.Value);
        }

        if (EndOfWeek().Match(norm) is { Success: true } eow)
        {
            var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)today.DayOfWeek + 7) % 7;
            if (daysUntilSunday == 0)
            {
                daysUntilSunday = 7;
            }

            return new DueMatch(today.AddDays(daysUntilSunday), eow.Value);
        }

        if (DayAfterTomorrow().Match(norm) is { Success: true } dat)
        {
            return new DueMatch(today.AddDays(2), dat.Value);
        }

        if (Tomorrow().Match(norm) is { Success: true } tmr)
        {
            return new DueMatch(today.AddDays(1), tmr.Value);
        }

        if (Days().Match(norm) is { Success: true } days && TryNumber(days.Groups["n"].Value, 1, out var d) && d > 0)
        {
            return new DueMatch(today.AddDays(d), days.Value);
        }

        if (Weeks().Match(norm) is { Success: true } weeks && TryNumber(weeks.Groups["n"].Value, 1, out var w) && w > 0)
        {
            return new DueMatch(today.AddDays(7 * w), weeks.Value);
        }

        if (Months().Match(norm) is { Success: true } months && TryNumber(months.Groups["n"].Value, 1, out var m) && m > 0)
        {
            return new DueMatch(today.AddMonths(m), months.Value);
        }

        return null;
    }

    private static DateOnly EndOfMonthDate(DateOnly date) => new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

    private static bool TryNumber(string group, int fallback, out int value)
    {
        if (string.IsNullOrEmpty(group))
        {
            value = fallback;
            return true;
        }

        var parsed = UzNumberParser.ParseSingle(group);
        if (parsed is null or > 3650)
        {
            value = 0;
            return false;
        }

        value = (int)parsed.Value;
        return true;
    }
}

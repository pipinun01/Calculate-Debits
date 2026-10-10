using System.Text.RegularExpressions;
using Debits.Application.Abstractions;
using Debits.Domain.Entities;
using Debits.Domain.Text;

namespace Debits.Application.Entries;

/// <summary>
/// Offline reading of "Ahmad aka 550 ming qarz, bir haftaga": amount via <see cref="UzNumberParser"/>, due date via
/// <see cref="DuePhraseParser"/>, intent by keywords, and whatever words are left are the customer name.
/// Used as the fallback when the LLM is unavailable and as a cross-check of its amount.
/// </summary>
public sealed partial class RegexEntryParser : IEntryParser
{
    public const string ParserName = "regex";

    /// <summary>Amounts below this without "ming"/"тысяч" are suspicious ("ellik" = 50 or 50 000?).</summary>
    public const long SmallAmount = 1000;

    public const string WarnAmountSmall = "amount_small";
    public const string WarnAmountMissing = "amount_missing";
    public const string WarnCustomerMissing = "customer_missing";
    public const string WarnAmountMismatch = "amount_mismatch";

    private static readonly string[] PaymentStems =
    [
        "toladi", "tolab", "tolov", "tuladi", "tulov", "tolandi", "berdi", "berib", "qaytardi", "qaytarib", "uzdi", "uzib", "yopdi", "tashladi", "tashlab",
        "oplatil", "oplatila", "oplata", "oplatili", "zaplatil", "zaplatila", "vernul", "vernula", "otdal", "otdala", "pogasil", "pogasila", "vnes", "vnesla", "prines", "prinesla",
    ];

    private static readonly string[] DebtStems =
    [
        "qarz", "karz", "qars", "qaris", "nasiya", "nasiyaga", "oldi", "olib", "dolg", "vzyal", "vzyala", "zanyal", "zanyala", "vdolg",
    ];

    /// <summary>Words that are never part of a name: verbs of the phrase, currency, fillers.</summary>
    private static readonly HashSet<string> Fillers =
    [
        "som", "sum", "summ", "sumov", "suma", "sumi", "uzs", "pul", "pulni", "pulini",
        "yoz", "yozing", "yozib", "yozdim", "qoy", "qoying", "qoyib", "ket", "ketdi", "qildi", "qilib", "boldi", "bor", "edi", "ekan", "uchun", "va", "ham", "yana", "bugun", "hozir",
        "muddat", "muddati", "muddatga", "srok", "srokom",
        "zapishi", "zapisat", "pishi", "v", "na", "za", "do", "i", "u", "eshe", "segodnya", "seychas", "nado", "nuzhno", "pozhaluysta", "iltimos",
        "dan", "ga", "ni", "ning",
    ];

    private static readonly string[] Honorifics = ["aka", "opa", "uka", "xola", "hola", "amaki", "bobo", "buvi", "hoji", "domla", "usta", "tog", "toga", "pochcha", "kelin", "singil", "oga"];

    private static readonly string[] CaseSuffixes = ["ning", "dan", "ga", "ni"];

    private static readonly string[] DurationStems = ["kun", "hafta", "oy", "den", "dnya", "dney", "dnej", "dn", "nedel", "mesyats"];

    [GeneratedRegex(@"[\p{L}\p{Nd}'’‘ʻʼ`´]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    public Task<ParsedEntry> ParseAsync(string text, DateOnly today, AppLanguage language, CancellationToken cancellationToken = default) =>
        Task.FromResult(Parse(text, today));

    public static ParsedEntry Parse(string text, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ParsedEntry.Empty(ParserName);
        }

        var words = WordRegex().Matches(text).Select(m => new Word(m.Value, m.Index, m.Length, SearchKey.NormalizeText(m.Value))).ToList();
        var used = new bool[words.Count];
        var warnings = new List<string>();

        // Due phrase: located in the normalized text, mapped back to the words it covers.
        var due = DuePhraseParser.Parse(text, today);
        if (due is not null)
        {
            var pos = 0;
            for (var i = 0; i < words.Count; i++)
            {
                var len = words[i].Norm.Length;
                if (len == 0)
                {
                    continue;
                }

                if (pos < due.Index + due.MatchedText.Length && pos + len > due.Index)
                {
                    used[i] = true;
                }

                pos += len + 1;
            }
        }

        // Amount: numbers that are not durations ("3 kunga") and not already inside the due phrase.
        var numbers = UzNumberParser.FindAll(text)
            .Where(n => !Overlaps(words, used, n.Start, n.End) && !IsDuration(words, n.End))
            .ToList();
        long? amount = null;
        if (numbers.Count > 0)
        {
            var preferred = numbers.Where(n => n.HasScaleWord || n.HasCurrencyWord).ToList();
            var best = (preferred.Count > 0 ? preferred : numbers).MaxBy(n => n.Value);
            if (best.Value > 0)
            {
                amount = best.Value;
                Mark(words, used, best.Start, best.End);
                if (best.Value < SmallAmount && !best.HasScaleWord)
                {
                    warnings.Add(WarnAmountSmall);
                }
            }
        }

        if (amount is null)
        {
            warnings.Add(WarnAmountMissing);
        }

        // Intent: a payment word wins over a debt word ("qarzini berdi" is a payment).
        var isPayment = false;
        var isDebt = false;
        for (var i = 0; i < words.Count; i++)
        {
            var n = words[i].Norm;
            if (PaymentStems.Any(s => n.StartsWith(s, StringComparison.Ordinal)))
            {
                isPayment = true;
                used[i] = true;
            }
            else if (DebtStems.Any(s => n.StartsWith(s, StringComparison.Ordinal)))
            {
                isDebt = true;
                used[i] = true;
            }
            else if (Fillers.Contains(n) || n.Length == 0)
            {
                used[i] = true;
            }
        }

        TransactionType? intent = isPayment ? TransactionType.Payment : isDebt ? TransactionType.Debt : null;

        var name = BuildName(words, used);
        if (name is null)
        {
            warnings.Add(WarnCustomerMissing);
        }

        var confidence = 0.2 + (amount is null ? 0 : 0.35) + (name is null ? 0 : 0.25) + (intent is null ? 0 : 0.2) - (warnings.Contains(WarnAmountSmall) ? 0.15 : 0);
        return new ParsedEntry(
            intent,
            name,
            amount,
            intent == TransactionType.Payment ? null : due?.DueDate,
            null,
            Math.Round(Math.Clamp(confidence, 0, 1), 2),
            ParserName,
            warnings);
    }

    /// <summary>The first run of unused words is the name; leftovers after a gap are ignored ("Ahmad aka 550 ming … rahmat").</summary>
    private static string? BuildName(List<Word> words, bool[] used)
    {
        var parts = new List<string>();
        for (var i = 0; i < words.Count; i++)
        {
            if (used[i] || words[i].Norm.All(char.IsDigit))
            {
                if (parts.Count > 0)
                {
                    break;
                }

                continue;
            }

            parts.Add(words[i].Text);
        }

        if (parts.Count == 0)
        {
            return null;
        }

        parts[^1] = StripCase(parts[^1]);
        var name = string.Join(' ', parts).Trim();
        return name.Length == 0 ? null : name;
    }

    /// <summary>"akaga" → "aka", "opadan" → "opa": only after a known honorific, so "Olga" stays "Olga".</summary>
    private static string StripCase(string word)
    {
        var norm = SearchKey.NormalizeText(word);
        foreach (var suffix in CaseSuffixes)
        {
            if (norm.Length > suffix.Length && norm.EndsWith(suffix, StringComparison.Ordinal) && Honorifics.Contains(norm[..^suffix.Length]))
            {
                // Suffix length is the same in Latin and Cyrillic except "нинг" (4) = "ning" (4), "дан" = "dan", "га" = "ga", "ни" = "ni".
                return word[..^suffix.Length];
            }
        }

        return word;
    }

    private static bool Overlaps(List<Word> words, bool[] used, int start, int end)
    {
        for (var i = 0; i < words.Count; i++)
        {
            if (used[i] && words[i].Start < end && words[i].End > start)
            {
                return true;
            }
        }

        return false;
    }

    private static void Mark(List<Word> words, bool[] used, int start, int end)
    {
        for (var i = 0; i < words.Count; i++)
        {
            if (words[i].Start < end && words[i].End > start)
            {
                used[i] = true;
            }
        }
    }

    private static bool IsDuration(List<Word> words, int numberEnd)
    {
        var next = words.FirstOrDefault(w => w.Start >= numberEnd);
        return next is not null && DurationStems.Any(s => next.Norm.StartsWith(s, StringComparison.Ordinal) && (s != "oy" || next.Norm is "oy" or "oyga" or "oylik" or "oygacha" or "oydan"));
    }

    private sealed record Word(string Text, int Start, int Length, string Norm)
    {
        public int End => Start + Length;
    }
}

using System.Text;

namespace Debits.Domain.Text;

/// <summary>
/// Normalized key for fuzzy customer lookup: lowercase, Cyrillic → Latin, apostrophes dropped,
/// anything that is not a letter or digit collapsed to a single space, and the letters that are
/// spelled interchangeably in names folded together (x→h: Ahmad/Axmad/Ахмад, q→k: Qodir/Kodir).
/// <see cref="NormalizeText"/> is the same without the folding, for parsers that match on words.
/// </summary>
public static class SearchKey
{
    private const string Apostrophes = "'’‘ʻʼ`´ʹˈ‛";
    private const string TurkicLetters = "şçığöü";

    public static string Normalize(string name, string? tag = null)
    {
        var raw = string.IsNullOrWhiteSpace(tag) ? name : $"{name} {tag}";
        return Normalize(raw);
    }

    public static string Normalize(string text) => NormalizeText(text).Replace('x', 'h').Replace('q', 'k');

    public static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var latin = UzTranslit.ToLatin(text.ToLowerInvariant());
        if (latin.AsSpan().IndexOfAny(TurkicLetters) >= 0)
        {
            // Speech recognition sometimes spells Uzbek the Turkish way: "beş" = besh, "kırk" = qirq.
            latin = latin.Replace("ş", "sh").Replace("ç", "ch").Replace('ı', 'i').Replace('ğ', 'g').Replace('ö', 'o').Replace('ü', 'u');
        }
        var sb = new StringBuilder(latin.Length);
        var pendingSpace = false;
        foreach (var ch in latin)
        {
            if (Apostrophes.Contains(ch))
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                }

                pendingSpace = false;
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                pendingSpace = true;
            }
        }

        return sb.ToString();
    }
}

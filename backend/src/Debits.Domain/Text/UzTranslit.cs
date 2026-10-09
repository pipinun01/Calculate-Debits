using System.Text;

namespace Debits.Domain.Text;

/// <summary>Uzbek (and Russian) Cyrillic → Uzbek Latin transliteration. Latin input passes through unchanged.</summary>
public static class UzTranslit
{
    private static readonly Dictionary<char, string> Map = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "yo",
        ['ж'] = "j", ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m",
        ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
        ['ф'] = "f", ['х'] = "x", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sh", ['ъ'] = "'",
        ['ы'] = "i", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
        ['ў'] = "o'", ['қ'] = "q", ['ғ'] = "g'", ['ҳ'] = "h",
    };

    private const string CyrillicVowels = "аеёиоуўэюя";

    public static bool HasCyrillic(string text) => text.Any(c => Map.ContainsKey(char.ToLowerInvariant(c)));

    public static string ToLatin(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var lower = char.ToLowerInvariant(ch);
            if (!Map.TryGetValue(lower, out var latin))
            {
                sb.Append(ch);
                continue;
            }

            // "е" at word start or after a vowel reads "ye" (Ергаш → Yergash, Саодат → unchanged).
            if (lower == 'е' && (i == 0 || !char.IsLetter(text[i - 1]) || CyrillicVowels.Contains(char.ToLowerInvariant(text[i - 1]))))
            {
                latin = "ye";
            }

            if (latin.Length == 0)
            {
                continue;
            }

            if (char.IsUpper(ch))
            {
                var nextIsLowerOrEnd = i + 1 >= text.Length || !char.IsUpper(text[i + 1]);
                sb.Append(nextIsLowerOrEnd || latin.Length == 1
                    ? char.ToUpperInvariant(latin[0]) + latin[1..]
                    : latin.ToUpperInvariant());
            }
            else
            {
                sb.Append(latin);
            }
        }

        return sb.ToString();
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace Debits.Domain.Text;

/// <summary>
/// Finds amounts in free text: digits with any grouping ("550 000", "1.500.000", "1,5 mln"),
/// number words in uz-Latn / uz-Cyrl / ru ("besh yuz ellik ming", "беш юз эллик минг", "пятьсот тысяч") and mixes ("550 ming").
/// </summary>
public static partial class UzNumberParser
{
    public readonly record struct NumberMatch(long Value, int Start, int Length, bool HasScaleWord, bool HasCurrencyWord)
    {
        public int End => Start + Length;
    }

    private enum TokenKind
    {
        Literal,
        Word,
    }

    private readonly record struct Token(TokenKind Kind, string Text, int Start, int Length, decimal Literal = 0)
    {
        public int End => Start + Length;
    }

    [GeneratedRegex(@"\d{1,3}(?:[  .,]\d{3})+(?!\d)|\d+(?:[.,]\d+)?|[\p{L}'’‘ʻʼ`´]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    /// <summary>Best amount candidate: prefers groups with a scale word (ming/million) or a currency word, then the largest value.</summary>
    public static long? ParseAmount(string text)
    {
        var matches = FindAll(text);
        if (matches.Count == 0)
        {
            return null;
        }

        var preferred = matches.Where(m => m.HasScaleWord || m.HasCurrencyWord).ToList();
        var pool = preferred.Count > 0 ? preferred : matches;
        return pool.MaxBy(m => m.Value).Value;
    }

    /// <summary>Parses a phrase that is expected to be a single number ("uch", "15", "ikki yuz"). Null if not a number.</summary>
    public static long? ParseSingle(string phrase)
    {
        var matches = FindAll(phrase);
        return matches.Count == 1 ? matches[0].Value : null;
    }

    public static IReadOnlyList<NumberMatch> FindAll(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var tokens = Tokenize(text);
        var result = new List<NumberMatch>();
        var i = 0;
        while (i < tokens.Count)
        {
            if (!IsNumberToken(tokens[i], out _))
            {
                i++;
                continue;
            }

            var start = i;
            decimal total = 0;
            decimal current = 0;
            var hasScale = false;
            var lastKind = (NumberTokenKind?)null;
            decimal lastScale = 0;

            while (i < tokens.Count && IsNumberToken(tokens[i], out var word) && (i == start || IsJoinable(text, tokens[i - 1], tokens[i])))
            {
                var token = tokens[i];

                // After a scale word ("550 ming" / "5 mln") a following number continues the group only when it is
                // itself closed by a smaller scale ("5 mln 200 ming"); otherwise it is a new number ("550 ming 3 kunga").
                if (lastKind == NumberTokenKind.Scale && (token.Kind == TokenKind.Literal || word.Kind != NumberTokenKind.Scale)
                    && !ContinuesWithSmallerScale(text, tokens, i, lastScale))
                {
                    break;
                }

                if (token.Kind == TokenKind.Literal)
                {
                    // A literal after a non-empty group starts a new number ("550 000 3" → two numbers).
                    if (current != 0 || lastKind == NumberTokenKind.Unit)
                    {
                        break;
                    }

                    current = token.Literal;
                    lastKind = NumberTokenKind.Unit;
                }
                else
                {
                    switch (word.Kind)
                    {
                        case NumberTokenKind.Unit:
                            // Two "big" units in a row (e.g. "besh besh") is a new number; small-after-big (yigirma besh) is additive.
                            if (lastKind == NumberTokenKind.Unit && current != 0 && word.Value >= current && word.Value >= 1)
                            {
                                goto EndGroup;
                            }

                            current += word.Value;
                            break;
                        case NumberTokenKind.Hundred:
                            current = (current == 0 ? 1 : current) * word.Value;
                            break;
                        case NumberTokenKind.Scale:
                            // Two scale words in a row with the second not smaller ("ming ming") start a new number.
                            if (lastKind == NumberTokenKind.Scale && word.Value >= lastScale)
                            {
                                goto EndGroup;
                            }

                            total += (current == 0 ? 1 : current) * word.Value;
                            current = 0;
                            hasScale = true;
                            lastScale = word.Value;
                            break;
                    }

                    lastKind = word.Kind;
                }

                i++;
            }

        EndGroup:
            var endToken = tokens[i - 1];
            var hasCurrency = i < tokens.Count && tokens[i].Kind == TokenKind.Word && NumberWords.CurrencyWords.Contains(NormalizeWord(tokens[i].Text));
            var value = total + current;
            result.Add(new NumberMatch(
                (long)Math.Round(value, MidpointRounding.AwayFromZero),
                tokens[start].Start,
                endToken.End - tokens[start].Start,
                hasScale,
                hasCurrency));
        }

        return result;
    }

    private static bool ContinuesWithSmallerScale(string text, List<Token> tokens, int from, decimal lastScale)
    {
        for (var j = from; j < tokens.Count; j++)
        {
            if (!IsNumberToken(tokens[j], out var w) || (j > from && !IsJoinable(text, tokens[j - 1], tokens[j])))
            {
                return false;
            }

            if (tokens[j].Kind == TokenKind.Word && w.Kind == NumberTokenKind.Scale)
            {
                return w.Value < lastScale;
            }
        }

        return false;
    }

    private static bool IsJoinable(string text, Token previous, Token next)
    {
        for (var k = previous.End; k < next.Start; k++)
        {
            var c = text[k];
            if (!char.IsWhiteSpace(c) && c != '-' && c != '–')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNumberToken(Token token, out NumberWord word)
    {
        if (token.Kind == TokenKind.Literal)
        {
            word = default;
            return true;
        }

        return NumberWords.TryGet(NormalizeWord(token.Text), out word);
    }

    internal static string NormalizeWord(string word) => SearchKey.NormalizeText(word).Replace(" ", string.Empty, StringComparison.Ordinal);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        foreach (Match m in TokenRegex().Matches(text))
        {
            if (char.IsDigit(m.Value[0]))
            {
                tokens.Add(new Token(TokenKind.Literal, m.Value, m.Index, m.Length, ParseLiteral(m.Value)));
            }
            else
            {
                tokens.Add(new Token(TokenKind.Word, m.Value, m.Index, m.Length));
            }
        }

        return tokens;
    }

    private static decimal ParseLiteral(string literal)
    {
        // Grouped thousands: "550 000", "1.500.000", "1,500,000"
        var grouped = literal.Length > 3 && Regex.IsMatch(literal, @"^\d{1,3}(?:[  .,]\d{3})+$");
        if (grouped)
        {
            var digits = new string(literal.Where(char.IsDigit).ToArray());
            return decimal.Parse(digits, CultureInfo.InvariantCulture);
        }

        // Decimal: "1,5" / "1.5"
        var normalized = literal.Replace(',', '.');
        return decimal.Parse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }
}

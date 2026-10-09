namespace Debits.Domain.Text;

internal enum NumberTokenKind
{
    /// <summary>Plain value that is added to the current group (bir, besh, yigirma, dvesti, …).</summary>
    Unit,
    /// <summary>Multiplies the current group by 100 (uz "yuz").</summary>
    Hundred,
    /// <summary>Closes the current group: ming / million / тысяча.</summary>
    Scale,
}

internal readonly record struct NumberWord(NumberTokenKind Kind, decimal Value);

/// <summary>Number words in uz-Latn, uz-Cyrl (via transliteration) and ru (via transliteration), apostrophes stripped.</summary>
internal static class NumberWords
{
    private static readonly Dictionary<string, NumberWord> Table = Build();

    public static bool TryGet(string normalizedWord, out NumberWord word) => Table.TryGetValue(normalizedWord, out word);

    public static readonly HashSet<string> CurrencyWords =
    [
        "som", "sum", "sumov", "suma", "sumi", "uzs", "sm", "mingsom", "mingsum",
    ];

    private static Dictionary<string, NumberWord> Build()
    {
        var d = new Dictionary<string, NumberWord>(StringComparer.Ordinal);

        void Unit(decimal value, params string[] words)
        {
            foreach (var w in words)
            {
                d[w] = new NumberWord(NumberTokenKind.Unit, value);
            }
        }

        void Scale(decimal value, params string[] words)
        {
            foreach (var w in words)
            {
                d[w] = new NumberWord(NumberTokenKind.Scale, value);
            }
        }

        // Uzbek (Latin; Cyrillic forms transliterate to the same keys: бир→bir, тўрт→tort, ўн→on, эллик→ellik …)
        Unit(0, "nol");
        Unit(1, "bir");
        Unit(2, "ikki");
        Unit(3, "uch");
        Unit(4, "tort");
        Unit(5, "besh");
        Unit(6, "olti");
        Unit(7, "yetti");
        Unit(8, "sakkiz");
        Unit(9, "toqqiz");
        Unit(10, "on");
        Unit(20, "yigirma");
        Unit(30, "ottiz");
        Unit(40, "qirq");
        Unit(50, "ellik");
        Unit(60, "oltmish");
        Unit(70, "yetmish");
        Unit(80, "sakson");
        Unit(90, "toqson");
        Unit(0.5m, "yarim");
        d["yuz"] = new NumberWord(NumberTokenKind.Hundred, 100);
        Scale(1_000, "ming", "min", "k");
        Scale(1_000_000, "million", "mln", "milion", "mil");

        // Russian (transliterated: пятьсот→pyatsot, тысяч→tisyach, полтора→poltora …)
        Unit(1, "odin", "odna", "odnu", "odno");
        Unit(2, "dva", "dve");
        Unit(3, "tri");
        Unit(4, "chetire");
        Unit(5, "pyat");
        Unit(6, "shest");
        Unit(7, "sem");
        Unit(8, "vosem");
        Unit(9, "devyat");
        Unit(10, "desyat");
        Unit(11, "odinnadtsat");
        Unit(12, "dvenadtsat");
        Unit(13, "trinadtsat");
        Unit(14, "chetirnadtsat");
        Unit(15, "pyatnadtsat");
        Unit(16, "shestnadtsat");
        Unit(17, "semnadtsat");
        Unit(18, "vosemnadtsat");
        Unit(19, "devyatnadtsat");
        Unit(20, "dvadtsat");
        Unit(30, "tridtsat");
        Unit(40, "sorok");
        Unit(50, "pyatdesyat");
        Unit(60, "shestdesyat");
        Unit(70, "semdesyat");
        Unit(80, "vosemdesyat");
        Unit(90, "devyanosto");
        Unit(100, "sto");
        Unit(200, "dvesti");
        Unit(300, "trista");
        Unit(400, "chetiresta");
        Unit(500, "pyatsot");
        Unit(600, "shestsot");
        Unit(700, "semsot");
        Unit(800, "vosemsot");
        Unit(900, "devyatsot");
        Unit(0.5m, "pol", "polovina", "polovinu");
        Unit(1.5m, "poltora", "poltori");
        Scale(1_000, "tisyacha", "tisyachi", "tisyach", "tis", "tisyachu");
        Scale(1_000_000, "milliona", "millionov", "millionа", "lyam", "lyama", "lyamov");

        return d;
    }
}

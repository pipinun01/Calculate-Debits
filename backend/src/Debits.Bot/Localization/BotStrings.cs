using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Debits.Domain.Entities;

namespace Debits.Bot.Localization;

/// <summary>Bot strings from embedded <c>bot.{uz-Latn,uz-Cyrl,ru}.json</c>. Missing key → uz-Latn → the key itself.</summary>
public sealed class BotStrings
{
    private readonly Dictionary<AppLanguage, Dictionary<string, string>> _tables;

    public BotStrings()
    {
        _tables = Enum.GetValues<AppLanguage>().ToDictionary(l => l, Load);
    }

    public IReadOnlyDictionary<string, string> Table(AppLanguage language) => _tables[language];

    public string Get(AppLanguage language, string key, params object?[] args)
    {
        if (!_tables[language].TryGetValue(key, out var template) && !_tables[AppLanguage.UzLatn].TryGetValue(key, out template))
        {
            return key;
        }

        return args.Length == 0 ? template : string.Format(CultureInfo.InvariantCulture, template, args);
    }

    private static Dictionary<string, string> Load(AppLanguage language)
    {
        var assembly = typeof(BotStrings).Assembly;
        var name = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith($"bot.{language.ToCode()}.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"Empty bot strings for {language}");
    }
}

using Debits.Bot.Localization;
using Debits.Domain.Entities;

namespace Debits.Integration.Tests.Bot;

public class BotStringsTests
{
    [Fact]
    public void AllLanguages_HaveTheSameKeys()
    {
        var strings = new BotStrings();
        var reference = strings.Table(AppLanguage.UzLatn).Keys.OrderBy(k => k).ToList();
        reference.Should().NotBeEmpty();

        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            strings.Table(language).Keys.OrderBy(k => k).Should().Equal(reference, $"{language} must have the same keys as uz-Latn");
        }
    }

    [Fact]
    public void Placeholders_MatchAcrossLanguages()
    {
        var strings = new BotStrings();
        foreach (var (key, template) in strings.Table(AppLanguage.UzLatn))
        {
            var expected = Placeholders(template);
            foreach (var language in Enum.GetValues<AppLanguage>())
            {
                Placeholders(strings.Table(language)[key]).Should().BeEquivalentTo(expected, $"{key} in {language}");
            }
        }
    }

    [Fact]
    public void Get_FormatsArguments_AndFallsBackToKey()
    {
        var strings = new BotStrings();
        strings.Get(AppLanguage.Ru, "wizard.due.days", 15).Should().Be("15 дней");
        strings.Get(AppLanguage.Ru, "no.such.key").Should().Be("no.such.key");
    }

    private static HashSet<string> Placeholders(string template) =>
        System.Text.RegularExpressions.Regex.Matches(template, @"\{\d\}").Select(m => m.Value).ToHashSet();
}

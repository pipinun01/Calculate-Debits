using System.Globalization;
using System.Text.Json;
using Debits.Application.Entries;
using Debits.Domain.Entities;

namespace Debits.Application.Tests.Entries;

/// <summary>Golden file <c>tests/parser-cases.jsonl</c>: one phrase per line with the expected reading.</summary>
public class RegexEntryParserGoldenTests
{
    public static IEnumerable<object?[]> Cases()
    {
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "parser-cases.jsonl")))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            yield return
            [
                r.GetProperty("text").GetString(),
                r.GetProperty("today").GetString(),
                r.GetProperty("intent").GetString(),
                r.GetProperty("customer").GetString(),
                r.GetProperty("amount").ValueKind == JsonValueKind.Null ? null : r.GetProperty("amount").GetInt64(),
                r.GetProperty("due").GetString(),
            ];
        }
    }

    [Fact]
    public void GoldenFile_HasEnoughCases() => Cases().Count().Should().BeGreaterThanOrEqualTo(60);

    [Theory]
    [MemberData(nameof(Cases))]
    public void Parses(string text, string today, string? intent, string? customer, long? amount, string? due)
    {
        var result = RegexEntryParser.Parse(text, DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture));

        var actualIntent = result.Intent switch { TransactionType.Debt => "debt", TransactionType.Payment => "payment", _ => null };
        (actualIntent, result.CustomerName, result.AmountUzs, result.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Should().Be((intent, customer, amount, due));
    }

    [Fact]
    public void Warnings_FlagSmallAndMissingValues()
    {
        var today = new DateOnly(2026, 10, 9);
        RegexEntryParser.Parse("Sardor ellik", today).Warnings.Should().Contain(RegexEntryParser.WarnAmountSmall);
        RegexEntryParser.Parse("Sardor ellik ming", today).Warnings.Should().BeEmpty();
        RegexEntryParser.Parse("Ahmad aka qarz", today).Warnings.Should().Contain(RegexEntryParser.WarnAmountMissing);
        RegexEntryParser.Parse("300 ming qarz", today).Warnings.Should().Contain(RegexEntryParser.WarnCustomerMissing);
        RegexEntryParser.Parse("  ", today).Should().BeEquivalentTo(Abstractions.ParsedEntry.Empty(RegexEntryParser.ParserName));
    }

    [Fact]
    public void Payment_NeverCarriesDueDate()
    {
        RegexEntryParser.Parse("Nodir uka 120 ming to'ladi bir haftaga", new DateOnly(2026, 10, 9)).DueDate.Should().BeNull();
    }
}

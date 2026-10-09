using Debits.Domain.Entities;

namespace Debits.Application.Abstractions;

/// <summary>Structured reading of "Ahmad aka 550 ming qarz, bir haftaga".</summary>
public sealed record ParsedEntry(
    TransactionType? Intent,
    string? CustomerName,
    long? AmountUzs,
    DateOnly? DueDate,
    string? Comment,
    double Confidence,
    string Parser,
    IReadOnlyList<string> Warnings)
{
    public static ParsedEntry Empty(string parser) => new(null, null, null, null, null, 0, parser, []);
}

public interface IEntryParser
{
    Task<ParsedEntry> ParseAsync(string text, DateOnly today, AppLanguage language, CancellationToken cancellationToken = default);
}

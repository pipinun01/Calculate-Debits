using System.Diagnostics;
using Debits.Application.Abstractions;
using Debits.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Debits.Application.Entries;

/// <summary>LLM reading of an entry phrase. Throws on transport / format errors; the composite falls back to regex.</summary>
public interface ILlmEntryParser
{
    bool IsConfigured { get; }

    Task<ParsedEntry> ParseAsync(string text, DateOnly today, AppLanguage language, CancellationToken cancellationToken = default);
}

/// <summary>
/// LLM first, regex always: regex fills what the LLM left empty, takes over when the LLM fails,
/// and a disagreement about the amount becomes a warning on the confirmation card.
/// </summary>
public sealed class CompositeEntryParser(IEnumerable<ILlmEntryParser> llms, ILogger<CompositeEntryParser> logger) : IEntryParser
{
    public const int MaxTextLength = 1000;

    private readonly ILlmEntryParser? _llm = llms.FirstOrDefault(l => l.IsConfigured);

    public async Task<ParsedEntry> ParseAsync(string text, DateOnly today, AppLanguage language, CancellationToken cancellationToken = default)
    {
        text = text.Trim();
        if (text.Length > MaxTextLength)
        {
            text = text[..MaxTextLength];
        }

        var regex = RegexEntryParser.Parse(text, today);
        // No amount and no debt/payment word: small talk, not worth an LLM call (and its share of the rate limit).
        if (_llm is null || text.Length == 0 || (regex.AmountUzs is null && regex.Intent is null))
        {
            return regex;
        }

        ParsedEntry llm;
        var sw = Stopwatch.StartNew();
        try
        {
            llm = await _llm.ParseAsync(text, today, language, cancellationToken);
            logger.LogInformation("LLM entry parse took {ElapsedMs} ms (confidence {Confidence})", sw.ElapsedMilliseconds, llm.Confidence);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LLM entry parse failed after {ElapsedMs} ms; using regex", sw.ElapsedMilliseconds);
            return regex;
        }

        var warnings = new List<string>(llm.Warnings);
        var amount = llm.AmountUzs is > 0 ? llm.AmountUzs : regex.AmountUzs;
        if (llm.AmountUzs is > 0 && regex.AmountUzs is > 0 && llm.AmountUzs != regex.AmountUzs)
        {
            // Number words are parsed deterministically; an LLM can drop a zero ("besh yuz ellik ming" → 55 000).
            // A bare small number is the one case where the regex has nothing better to offer.
            warnings.Add(RegexEntryParser.WarnAmountMismatch);
            if (regex.AmountUzs >= RegexEntryParser.SmallAmount)
            {
                amount = regex.AmountUzs;
            }
        }

        if (amount is null)
        {
            warnings.Add(RegexEntryParser.WarnAmountMissing);
        }
        else if (amount < RegexEntryParser.SmallAmount)
        {
            warnings.Add(RegexEntryParser.WarnAmountSmall);
        }

        var name = string.IsNullOrWhiteSpace(llm.CustomerName) ? regex.CustomerName : llm.CustomerName.Trim();
        if (name is null)
        {
            warnings.Add(RegexEntryParser.WarnCustomerMissing);
        }

        var intent = llm.Intent ?? regex.Intent;
        return new ParsedEntry(
            intent,
            name,
            amount,
            intent == TransactionType.Payment ? null : llm.DueDate ?? regex.DueDate,
            string.IsNullOrWhiteSpace(llm.Comment) ? null : llm.Comment.Trim(),
            llm.Confidence,
            llm.Parser,
            warnings.Distinct().ToList());
    }
}

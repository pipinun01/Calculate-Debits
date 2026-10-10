using Debits.Api.Auth;
using Debits.Application.Abstractions;
using Debits.Application.Entries;
using Debits.Domain;
using Debits.Domain.Customers;
using Debits.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Debits.Api.Controllers;

/// <summary>Voice / text → parsed entry for the Mini App form. Same pipeline as the bot; writes nothing.</summary>
[ApiController]
[Authorize(Policy = Policies.ShopMember)]
[Route("api/entries")]
public sealed class EntriesController(EntryDraftService drafts, ISpeechToText stt) : ControllerBase
{
    public const long MaxAudioBytes = 10 * 1024 * 1024;

    [HttpPost("parse")]
    [Consumes("application/json")]
    public async Task<ActionResult<EntryParseResponse>> ParseText([FromBody] ParseEntryRequest request, CancellationToken ct)
    {
        var text = request.Text?.Trim() ?? string.Empty;
        if (text.Length is 0 or > CompositeEntryParser.MaxTextLength)
        {
            throw new DomainException("entry.text_invalid", $"Text must be 1..{CompositeEntryParser.MaxTextLength} characters");
        }

        return EntryParseResponse.From(await drafts.BuildAsync(text, ct));
    }

    /// <summary>multipart/form-data with one file field <c>audio</c> (webm / ogg / mp3 / m4a / wav).</summary>
    [HttpPost("parse")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxAudioBytes)]
    public async Task<ActionResult<EntryParseResponse>> ParseAudio(IFormFile? audio, CancellationToken ct)
    {
        if (audio is null || audio.Length == 0)
        {
            throw new DomainException("entry.audio_missing", "Form field 'audio' with the recording is required");
        }

        if (!stt.IsConfigured)
        {
            throw new DomainException("voice.unavailable", "Speech recognition is not configured");
        }

        Transcript transcript;
        await using (var stream = audio.OpenReadStream())
        {
            try
            {
                var names = await drafts.RecentCustomerNamesAsync(ct: ct);
                transcript = await stt.TranscribeAsync(stream, string.IsNullOrWhiteSpace(audio.FileName) ? "audio.webm" : audio.FileName, names, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new DomainException("voice.failed", "Speech recognition failed: " + ex.StatusCode);
            }
        }

        if (string.IsNullOrWhiteSpace(transcript.Text))
        {
            throw new DomainException("voice.empty", "Nothing was recognised in the recording");
        }

        return EntryParseResponse.From(await drafts.BuildAsync(transcript.Text, ct));
    }
}

public sealed record ParseEntryRequest(string? Text);

public sealed record EntryCandidate(Guid Id, string Name, string? Tag, string? Phone, double Similarity);

public sealed record EntryParseResponse(
    string Transcript,
    TransactionType Type,
    long? Amount,
    DateOnly? DueDate,
    bool DueDateIsDefault,
    string? Comment,
    string? CustomerName,
    /// <summary>exact | ambiguous | none</summary>
    string CustomerStatus,
    Guid? CustomerId,
    IReadOnlyList<EntryCandidate> Candidates,
    double Confidence,
    string Parser,
    IReadOnlyList<string> Warnings)
{
    public static EntryParseResponse From(EntryDraft d)
    {
        var (status, id, candidates) = d.Customer switch
        {
            CustomerResolution.Exact e => ("exact", (Guid?)e.Customer.CustomerId, new[] { e.Customer }.AsEnumerable()),
            CustomerResolution.Ambiguous a => ("ambiguous", null, a.Candidates),
            _ => ("none", null, []),
        };
        return new EntryParseResponse(d.Text, d.Type, d.Amount, d.DueDate, d.DueDateIsDefault, d.Comment, d.CustomerName, status, id,
            candidates.Select(c => new EntryCandidate(c.CustomerId, c.Name, c.Tag, c.Phone, Math.Round(c.Similarity, 3))).ToList(),
            d.Confidence, d.Parser, d.Warnings);
    }
}

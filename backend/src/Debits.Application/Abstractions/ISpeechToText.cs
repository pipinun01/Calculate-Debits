namespace Debits.Application.Abstractions;

public sealed record Transcript(string Text, string? DetectedLanguage, TimeSpan Duration);

public interface ISpeechToText
{
    /// <summary>False when no API key is set: the bot then asks to type the entry instead.</summary>
    bool IsConfigured { get; }

    /// <param name="knownNames">Customer names of the shop; recognisers use them as a spelling hint so "Nodir" does not come back as "Nador".</param>
    Task<Transcript> TranscribeAsync(Stream audio, string fileName, IReadOnlyList<string>? knownNames = null, CancellationToken cancellationToken = default);
}

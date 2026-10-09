namespace Debits.Application.Abstractions;

public sealed record Transcript(string Text, string? DetectedLanguage, TimeSpan Duration);

public interface ISpeechToText
{
    Task<Transcript> TranscribeAsync(Stream audio, string fileName, CancellationToken cancellationToken = default);
}

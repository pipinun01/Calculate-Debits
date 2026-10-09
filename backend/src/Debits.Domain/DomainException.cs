namespace Debits.Domain;

public class DomainException(string code, string message) : Exception(message)
{
    /// <summary>Stable machine-readable code; the API maps it to ProblemDetails and the i18n key.</summary>
    public string Code { get; } = code;
}

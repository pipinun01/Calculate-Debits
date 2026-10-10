using System.Text.Json;
using System.Text.Json.Serialization;
using Debits.Domain.Entities;

namespace Debits.Bot.Entries;

public sealed class PendingCandidate
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Tag { get; set; }

    public string Display => string.IsNullOrWhiteSpace(Tag) ? Name : $"{Name} ({Tag})";
}

/// <summary>State of an entry awaiting confirmation; stored as jsonb in <c>pending_entries.payload</c>.</summary>
public sealed class PendingPayload
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ShopName { get; set; } = string.Empty;
    public TransactionType Type { get; set; }
    public long? Amount { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? Comment { get; set; }

    /// <summary>Existing customer chosen for the entry.</summary>
    public Guid? CustomerId { get; set; }
    public string? CustomerDisplay { get; set; }

    /// <summary>Name as heard / typed; used for search and for creating a new customer.</summary>
    public string? SpokenName { get; set; }

    /// <summary>The user asked to create <see cref="SpokenName"/> (+ <see cref="NewTag"/>) on confirmation.</summary>
    public bool CreateNew { get; set; }
    public string? NewTag { get; set; }
    public List<PendingCandidate> Candidates { get; set; } = [];

    public string Transcript { get; set; } = string.Empty;
    public EntrySource Source { get; set; }
    public string? VoiceFileId { get; set; }
    public string? Parser { get; set; }
    public List<string> Warnings { get; set; } = [];

    /// <summary>Set once confirmed; cleared again when the entry is deleted from the receipt.</summary>
    public Guid? TransactionId { get; set; }

    [JsonIgnore]
    public bool HasCustomer => CustomerId is not null || (CreateNew && !string.IsNullOrWhiteSpace(SpokenName));

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static PendingPayload Deserialize(string json) => JsonSerializer.Deserialize<PendingPayload>(json, Json) ?? new PendingPayload();
}

/// <summary>Conversation data while the bot waits for a typed amount / term / name / tag.</summary>
public sealed class EntryInputState
{
    public long PendingId { get; set; }
}

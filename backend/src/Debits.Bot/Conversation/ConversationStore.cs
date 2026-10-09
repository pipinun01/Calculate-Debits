using System.Text.Json;
using Debits.Application.Abstractions;
using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Debits.Bot.Conversation;

/// <summary>Per-user multi-step state in <c>conversation_states</c>: which step the user is at and the data collected so far.</summary>
public sealed class ConversationStore(IDebitsDbContext db, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<(string State, T Data)?> GetAsync<T>(Guid userId, string statePrefix, CancellationToken ct = default)
        where T : class, new()
    {
        var row = await db.ConversationStates.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId, ct);
        if (row is null || !row.State.StartsWith(statePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var data = JsonSerializer.Deserialize<T>(row.Data, Json) ?? new T();
        return (row.State, data);
    }

    public async Task<string?> GetStateAsync(Guid userId, CancellationToken ct = default) =>
        await db.ConversationStates.AsNoTracking().Where(s => s.UserId == userId).Select(s => s.State).SingleOrDefaultAsync(ct);

    public async Task SetAsync<T>(Guid userId, string state, T data, CancellationToken ct = default)
    {
        var row = await db.ConversationStates.SingleOrDefaultAsync(s => s.UserId == userId, ct);
        if (row is null)
        {
            row = new ConversationState { UserId = userId };
            db.ConversationStates.Add(row);
        }

        row.State = state;
        row.Data = JsonSerializer.Serialize(data, Json);
        row.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Returns true when there was a state to clear.</summary>
    public async Task<bool> ClearAsync(Guid userId, CancellationToken ct = default)
    {
        var row = await db.ConversationStates.SingleOrDefaultAsync(s => s.UserId == userId, ct);
        if (row is null)
        {
            return false;
        }

        db.ConversationStates.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

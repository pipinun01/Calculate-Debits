using Debits.Application.Abstractions;
using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Debits.Bot.Entries;

/// <summary>Housekeeping for <c>pending_entries</c>: unanswered cards become <c>expired</c>, rows older than a day are removed.</summary>
public sealed class PendingEntrySweeper(IDebitsDbContext db, TimeProvider clock)
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public async Task<(int Expired, int Deleted)> SweepAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expired = await db.PendingEntries
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(p => p.Status == PendingEntryStatus.Pending && p.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PendingEntryStatus.Expired), ct);

        var cutoff = now - Retention;
        var deleted = await db.PendingEntries
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(p => p.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        return (expired, deleted);
    }
}

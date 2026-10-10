using System.Text.Json;
using Debits.Application.Abstractions;
using Debits.Application.Tenancy;
using Debits.Domain.Entities;

namespace Debits.Application.Audit;

/// <summary>Appends an <c>audit_events</c> row to the current unit of work; the caller's SaveChanges flushes it.</summary>
public sealed class AuditWriter(IDebitsDbContext db, ITenantContext tenant)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Write(string action, string entity, object entityId, object? data = null)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            MemberId = tenant.MemberId,
            Action = action,
            Entity = entity,
            EntityId = entityId.ToString() ?? string.Empty,
            Data = data is null ? null : JsonSerializer.Serialize(data, Json),
        });
    }
}

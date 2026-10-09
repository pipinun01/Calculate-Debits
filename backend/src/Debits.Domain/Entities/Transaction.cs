namespace Debits.Domain.Entities;

public sealed class Transaction : ITenantEntity, IHasCreatedAt
{
    /// <summary>Workers may edit/delete their own entries this long after creation.</summary>
    public static readonly TimeSpan WorkerEditWindow = TimeSpan.FromMinutes(10);

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ShopId { get; set; }
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public TransactionType Type { get; set; }
    public long Amount { get; set; }
    public string? Comment { get; set; }
    public DateOnly? DueDate { get; set; }
    public Guid? TargetDebtId { get; set; }
    public EntrySource Source { get; set; } = EntrySource.Manual;
    public string? VoiceFileId { get; set; }
    public string? Transcript { get; set; }
    public Guid CreatedByMemberId { get; set; }
    public ShopMember CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public Guid? UpdatedByMemberId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public Guid? DeletedByMemberId { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeleteReason { get; set; }

    public bool IsDebt => Type == TransactionType.Debt;

    public void SoftDelete(Guid byMemberId, DateTime utcNow, string? reason = null)
    {
        IsDeleted = true;
        DeletedByMemberId = byMemberId;
        DeletedAt = utcNow;
        DeleteReason = reason;
    }

    public bool IsWithinWorkerEditWindow(DateTime utcNow) => utcNow - CreatedAt <= WorkerEditWindow;
}

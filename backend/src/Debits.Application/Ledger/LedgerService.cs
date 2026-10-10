using Debits.Application.Abstractions;
using Debits.Application.Audit;
using Debits.Application.Common;
using Debits.Application.Tenancy;
using Debits.Domain;
using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Debits.Application.Ledger;

/// <summary>
/// All ledger mutations: create / edit / soft-delete a transaction under the customer row lock, then rebuild allocations.
/// Owners may touch any entry; workers only their own within <see cref="Transaction.WorkerEditWindow"/>.
/// </summary>
public sealed class LedgerService(IDebitsDbContext db, ITenantContext tenant, TimeProvider clock, AllocationSyncService allocations, AuditWriter audit)
{
    public const long MaxAmount = 1_000_000_000_000; // 1 trillion so'm
    public const int MaxCommentLength = 512;

    public async Task<LedgerMutation> CreateAsync(CreateTransactionCommand cmd, CancellationToken ct = default)
    {
        var memberId = RequireMember();
        ValidateAmount(cmd.Amount);
        var comment = NormalizeComment(cmd.Comment);

        var shop = await db.Shops.SingleAsync(s => s.Id == tenant.ShopId, ct);
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == cmd.CustomerId, ct)
            ?? throw new NotFoundException("customer.not_found", "Customer not found");

        var today = LocalTime.Today(clock, shop.Timezone);
        DateOnly? dueDate = cmd.Type == TransactionType.Debt ? cmd.DueDate ?? today.AddDays(shop.DefaultDueDays) : null;
        if (cmd.Type == TransactionType.Payment && cmd.DueDate is not null)
        {
            throw new DomainException("transaction.due_date_for_payment", "Payments have no due date");
        }

        return await db.InTransactionAsync(async ct2 =>
        {
            await db.LockCustomerAsync(customer.Id, ct2);
            var targetDebtId = await ResolveTargetDebtAsync(cmd.Type, cmd.TargetDebtId, customer.Id, excludeId: null, ct2);

            var tx = new Transaction
            {
                CustomerId = customer.Id,
                Type = cmd.Type,
                Amount = cmd.Amount,
                Comment = comment,
                DueDate = dueDate,
                TargetDebtId = targetDebtId,
                Source = cmd.Source,
                VoiceFileId = cmd.VoiceFileId,
                Transcript = cmd.Transcript,
                CreatedByMemberId = memberId,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
            };
            db.Transactions.Add(tx);
            audit.Write("transaction.create", "transaction", tx.Id, new { tx.CustomerId, tx.Type, tx.Amount, tx.DueDate, tx.Source });
            await db.SaveChangesAsync(ct2);

            var result = await allocations.RecalculateAsync(customer.Id, ct2);
            return new LedgerMutation(await ToDtoAsync(tx, result, ct2), result.Balance);
        }, ct);
    }

    public async Task<LedgerMutation> UpdateAsync(Guid id, UpdateTransactionCommand cmd, CancellationToken ct = default)
    {
        var memberId = RequireMember();
        var tx = await db.Transactions.SingleOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("transaction.not_found", "Transaction not found");
        EnsureCanModify(tx);

        return await db.InTransactionAsync(async ct2 =>
        {
            await db.LockCustomerAsync(tx.CustomerId, ct2);
            var before = new { tx.Amount, tx.Comment, tx.DueDate, tx.TargetDebtId };

            if (cmd.Amount is { } amount)
            {
                ValidateAmount(amount);
                tx.Amount = amount;
            }

            if (cmd.Comment is not null)
            {
                tx.Comment = NormalizeComment(cmd.Comment);
            }

            if (cmd.DueDate is { } due)
            {
                if (tx.Type != TransactionType.Debt)
                {
                    throw new DomainException("transaction.due_date_for_payment", "Payments have no due date");
                }

                tx.DueDate = due;
            }

            if (cmd.TargetDebtId is { } target)
            {
                tx.TargetDebtId = target == Guid.Empty ? null : await ResolveTargetDebtAsync(tx.Type, target, tx.CustomerId, tx.Id, ct2);
            }

            tx.UpdatedByMemberId = memberId;
            tx.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            audit.Write("transaction.update", "transaction", tx.Id, new { before, after = new { tx.Amount, tx.Comment, tx.DueDate, tx.TargetDebtId } });
            await db.SaveChangesAsync(ct2);

            var result = await allocations.RecalculateAsync(tx.CustomerId, ct2);
            return new LedgerMutation(await ToDtoAsync(tx, result, ct2), result.Balance);
        }, ct);
    }

    /// <summary>Soft delete; the row stays for audit and is hidden by the <c>SoftDelete</c> query filter.</summary>
    public async Task<long> DeleteAsync(Guid id, string? reason, CancellationToken ct = default)
    {
        var memberId = RequireMember();
        var tx = await db.Transactions.SingleOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("transaction.not_found", "Transaction not found");
        EnsureCanModify(tx);

        return await db.InTransactionAsync(async ct2 =>
        {
            await db.LockCustomerAsync(tx.CustomerId, ct2);
            tx.SoftDelete(memberId, clock.GetUtcNow().UtcDateTime, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
            audit.Write("transaction.delete", "transaction", tx.Id, new { tx.CustomerId, tx.Type, tx.Amount, tx.DeleteReason });
            await db.SaveChangesAsync(ct2);

            var result = await allocations.RecalculateAsync(tx.CustomerId, ct2);
            return result.Balance;
        }, ct);
    }

    public async Task<TransactionDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await Query(db.Transactions.Where(t => t.Id == id)).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("transaction.not_found", "Transaction not found");
        return dto;
    }

    /// <summary>Customer ledger, newest first.</summary>
    public async Task<Page<TransactionDto>> LedgerAsync(Guid customerId, string? cursor, int? limit, TransactionType? type = null, CancellationToken ct = default)
    {
        var offset = Cursor.Decode(cursor);
        var take = Cursor.ClampLimit(limit);
        var q = db.Transactions.Where(t => t.CustomerId == customerId);
        if (type is { } tt)
        {
            q = q.Where(t => t.Type == tt);
        }

        var rows = await Query(q.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)).Skip(offset).Take(take + 1).ToListAsync(ct);
        return Cursor.Build(rows, offset, take);
    }

    public bool CanModify(Guid createdByMemberId, DateTime createdAt)
    {
        if (tenant.MemberId is not { } me)
        {
            return false;
        }

        return tenant.IsOwner || (createdByMemberId == me && clock.GetUtcNow().UtcDateTime - createdAt <= Transaction.WorkerEditWindow);
    }

    /// <summary>Projection shared by every read; <c>CanEdit</c> is computed client-side after materialisation.</summary>
    internal IQueryable<TransactionDto> Query(IQueryable<Transaction> source)
    {
        var me = tenant.MemberId;
        var isOwner = tenant.IsOwner;
        var editUntil = clock.GetUtcNow().UtcDateTime - Transaction.WorkerEditWindow;
        return source.Select(t => new TransactionDto(
            t.Id,
            t.CustomerId,
            t.Type,
            t.Amount,
            t.Comment,
            t.DueDate,
            t.TargetDebtId,
            t.Source,
            t.CreatedByMemberId,
            t.CreatedBy.User.FirstName,
            t.CreatedAt,
            t.UpdatedAt,
            t.Type == TransactionType.Debt
                ? t.Amount - (db.PaymentAllocations.Where(a => a.DebtId == t.Id).Sum(a => (long?)a.Amount) ?? 0)
                : null,
            isOwner || (t.CreatedByMemberId == me && t.CreatedAt >= editUntil)));
    }

    private async Task<TransactionDto> ToDtoAsync(Transaction tx, Domain.Ledger.AllocationResult result, CancellationToken ct)
    {
        var name = await db.ShopMembers.Where(m => m.Id == tx.CreatedByMemberId).Select(m => m.User.FirstName).SingleAsync(ct);
        long? remaining = tx.Type == TransactionType.Debt ? result.Debts.FirstOrDefault(d => d.DebtId == tx.Id)?.Remaining ?? tx.Amount : null;
        return new TransactionDto(tx.Id, tx.CustomerId, tx.Type, tx.Amount, tx.Comment, tx.DueDate, tx.TargetDebtId, tx.Source,
            tx.CreatedByMemberId, name, tx.CreatedAt, tx.UpdatedAt, remaining, CanModify(tx.CreatedByMemberId, tx.CreatedAt));
    }

    private async Task<Guid?> ResolveTargetDebtAsync(TransactionType type, Guid? targetDebtId, Guid customerId, Guid? excludeId, CancellationToken ct)
    {
        if (targetDebtId is not { } target)
        {
            return null;
        }

        if (type != TransactionType.Payment)
        {
            throw new DomainException("transaction.target_debt_for_debt", "Only payments can target a debt");
        }

        var ok = await db.Transactions.AnyAsync(t => t.Id == target && t.CustomerId == customerId && t.Type == TransactionType.Debt && t.Id != excludeId, ct);
        if (!ok)
        {
            throw new DomainException("transaction.target_debt_invalid", "Target debt must be a debt of the same customer");
        }

        return target;
    }

    private Guid RequireMember() => tenant.MemberId ?? throw new ForbiddenException("tenant.required", "No shop member in context");

    private void EnsureCanModify(Transaction tx)
    {
        if (!CanModify(tx.CreatedByMemberId, tx.CreatedAt))
        {
            throw new ForbiddenException("transaction.forbidden", "Only owners, or the author within 10 minutes, may change this entry");
        }
    }

    private static void ValidateAmount(long amount)
    {
        if (amount <= 0 || amount > MaxAmount)
        {
            throw new DomainException("transaction.amount_invalid", $"Amount must be 1..{MaxAmount}");
        }
    }

    private static string? NormalizeComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        comment = comment.Trim();
        return comment.Length > MaxCommentLength ? comment[..MaxCommentLength] : comment;
    }
}

using Debits.Domain.Entities;
using Debits.Domain.Ledger;

namespace Debits.Domain.Tests.Ledger;

public class AllocationRecalculatorTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    private static LedgerEntry Debt(Guid id, long amount, int minute) =>
        new(id, TransactionType.Debt, amount, T0.AddMinutes(minute));

    private static LedgerEntry Payment(Guid id, long amount, int minute, Guid? target = null) =>
        new(id, TransactionType.Payment, amount, T0.AddMinutes(minute), target);

    private static Guid G(int n) => new(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    [Fact]
    public void Empty_HasNoAllocationsAndZeroBalance()
    {
        var r = AllocationRecalculator.Recalculate([]);
        r.Allocations.Should().BeEmpty();
        r.Balance.Should().Be(0);
        r.UnallocatedCredit.Should().Be(0);
    }

    [Fact]
    public void SingleDebt_StaysOpen()
    {
        var r = AllocationRecalculator.Recalculate([Debt(G(1), 100_000, 0)]);
        r.Debts.Single().Remaining.Should().Be(100_000);
        r.Balance.Should().Be(100_000);
    }

    [Fact]
    public void Payment_CoversOldestDebtFirst()
    {
        var r = AllocationRecalculator.Recalculate(
        [
            Debt(G(1), 100_000, 0),
            Debt(G(2), 50_000, 1),
            Payment(G(3), 120_000, 2),
        ]);

        r.Allocations.Should().BeEquivalentTo(
        [
            new Allocation(G(3), G(1), 100_000),
            new Allocation(G(3), G(2), 20_000),
        ]);
        r.Debts.Should().BeEquivalentTo(
        [
            new DebtState(G(1), 100_000, 0),
            new DebtState(G(2), 50_000, 30_000),
        ]);
        r.Balance.Should().Be(30_000);
        r.UnallocatedCredit.Should().Be(0);
    }

    [Fact]
    public void Overpayment_BecomesCredit_AndCoversNextDebt()
    {
        var r = AllocationRecalculator.Recalculate(
        [
            Debt(G(1), 100_000, 0),
            Payment(G(2), 150_000, 1),
            Debt(G(3), 80_000, 2),
        ]);

        r.Allocations.Should().BeEquivalentTo(
        [
            new Allocation(G(2), G(1), 100_000),
            new Allocation(G(2), G(3), 50_000),
        ]);
        r.Debts.Single(d => d.DebtId == G(3)).Remaining.Should().Be(30_000);
        r.UnallocatedCredit.Should().Be(0);
        r.Balance.Should().Be(30_000);
    }

    [Fact]
    public void Overpayment_WithoutLaterDebt_IsUnallocatedCredit()
    {
        var r = AllocationRecalculator.Recalculate(
        [
            Debt(G(1), 100_000, 0),
            Payment(G(2), 150_000, 1),
        ]);

        r.UnallocatedCredit.Should().Be(50_000);
        r.Balance.Should().Be(-50_000);
        r.OpenDebts.Should().BeEmpty();
    }

    [Fact]
    public void PaymentBeforeAnyDebt_IsCredit_ConsumedByFirstDebt()
    {
        var r = AllocationRecalculator.Recalculate(
        [
            Payment(G(1), 30_000, 0),
            Debt(G(2), 100_000, 1),
        ]);

        r.Allocations.Should().ContainSingle().Which.Should().Be(new Allocation(G(1), G(2), 30_000));
        r.Debts.Single().Remaining.Should().Be(70_000);
    }

    [Fact]
    public void DeletingPayment_ReopensDebt()
    {
        var before = AllocationRecalculator.Recalculate(
        [
            Debt(G(1), 100_000, 0),
            Payment(G(2), 60_000, 1),
        ]);
        before.Debts.Single().Remaining.Should().Be(40_000);

        // soft-deleted payment is simply not passed in
        var after = AllocationRecalculator.Recalculate([Debt(G(1), 100_000, 0)]);
        after.Allocations.Should().BeEmpty();
        after.Debts.Single().Remaining.Should().Be(100_000);
    }

    [Fact]
    public void EditingDebtAmount_RedistributesAllocations()
    {
        var entries = new List<LedgerEntry>
        {
            Debt(G(1), 100_000, 0),
            Debt(G(2), 100_000, 1),
            Payment(G(3), 120_000, 2),
        };
        AllocationRecalculator.Recalculate(entries).Debts.Should().BeEquivalentTo(
        [
            new DebtState(G(1), 100_000, 0),
            new DebtState(G(2), 100_000, 80_000),
        ]);

        entries[0] = Debt(G(1), 50_000, 0);
        AllocationRecalculator.Recalculate(entries).Debts.Should().BeEquivalentTo(
        [
            new DebtState(G(1), 50_000, 0),
            new DebtState(G(2), 100_000, 30_000),
        ]);
    }

    [Fact]
    public void TargetedPayment_CoversTargetFirst_ThenFifo()
    {
        var r = AllocationRecalculator.Recalculate(
        [
            Debt(G(1), 100_000, 0),
            Debt(G(2), 50_000, 1),
            Payment(G(3), 70_000, 2, target: G(2)),
        ]);

        r.Allocations.Should().BeEquivalentTo(
        [
            new Allocation(G(3), G(2), 50_000),
            new Allocation(G(3), G(1), 20_000),
        ]);
        r.Debts.Should().BeEquivalentTo(
        [
            new DebtState(G(1), 100_000, 80_000),
            new DebtState(G(2), 50_000, 0),
        ]);
    }

    [Fact]
    public void TargetedPayment_ToUnknownOrClosedDebt_FallsBackToFifo()
    {
        var r = AllocationRecalculator.Recalculate(
        [
            Debt(G(1), 100_000, 0),
            Payment(G(2), 30_000, 1, target: G(99)),
        ]);

        r.Allocations.Should().ContainSingle().Which.Should().Be(new Allocation(G(2), G(1), 30_000));
    }

    [Fact]
    public void Order_IsChronological_NotInputOrder()
    {
        var r = AllocationRecalculator.Recalculate(
        [
            Payment(G(3), 50_000, 5),
            Debt(G(2), 50_000, 3),
            Debt(G(1), 50_000, 1),
        ]);

        r.Allocations.Should().ContainSingle().Which.Should().Be(new Allocation(G(3), G(1), 50_000));
    }

    [Fact]
    public void MultiplePayments_ChainAcrossDebts()
    {
        var r = AllocationRecalculator.Recalculate(
        [
            Debt(G(1), 100_000, 0),
            Payment(G(2), 40_000, 1),
            Payment(G(3), 40_000, 2),
            Payment(G(4), 40_000, 3),
            Debt(G(5), 10_000, 4),
        ]);

        r.Allocations.Should().BeEquivalentTo(
        [
            new Allocation(G(2), G(1), 40_000),
            new Allocation(G(3), G(1), 40_000),
            new Allocation(G(4), G(1), 20_000),
            new Allocation(G(4), G(5), 10_000),
        ]);
        r.UnallocatedCredit.Should().Be(10_000);
        r.Balance.Should().Be(-10_000);
    }

    [Fact]
    public void NonPositiveAmount_Throws()
    {
        var act = () => AllocationRecalculator.Recalculate([Debt(G(1), 0, 0)]);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("ledger.invalid_amount");
    }
}

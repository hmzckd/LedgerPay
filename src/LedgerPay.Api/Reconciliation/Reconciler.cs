using LedgerPay.Api.Accounts;
using LedgerPay.Api.Data;
using LedgerPay.Api.Transfers;
using Microsoft.EntityFrameworkCore;
using static System.FormattableString;

namespace LedgerPay.Api.Reconciliation;

public class Reconciler(LedgerDbContext db, TimeProvider clock, ILogger<Reconciler> logger)
{
    private const int MaxListed = 10;

    public async Task<ReconciliationRun> RunManuallyAsync(DateOnly day, CancellationToken ct)
    {
        var run = ReconciliationRun.Start(day, ReconciliationKind.Manual, clock.GetUtcNow());
        await CheckAndFinish(run, ct);
        db.ReconciliationRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    public async Task<ReconciliationRun?> RunScheduledAsync(DateOnly day, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var run = ReconciliationRun.Start(day, ReconciliationKind.Scheduled, clock.GetUtcNow());
        db.ReconciliationRuns.Add(run);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            logger.LogInformation("{Day:O} was already reconciled by another copy of the app.", day);
            return null;
        }

        await CheckAndFinish(run, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return run;
    }

    private async Task CheckAndFinish(ReconciliationRun run, CancellationToken ct)
    {
        var problems = new List<string>();
        await CheckTheTotal(problems, ct);
        await CheckTheTransfersOf(run.Day, problems, ct);
        await CheckForCustomerAccountsBelowZero(problems, ct);
        run.Finish(problems, clock.GetUtcNow());

        if (run.Status == ReconciliationStatus.Failed)
        {
            logger.LogError(
                "{Kind} reconciliation of {Day:O} failed with {Count} problems: {Problems}",
                run.Kind, run.Day, problems.Count, string.Join(" ", problems));
        }
        else
        {
            logger.LogInformation("{Kind} reconciliation of {Day:O} passed.", run.Kind, run.Day);
        }
    }

    private async Task CheckTheTotal(List<string> problems, CancellationToken ct)
    {
        var total = await db.LedgerEntries.SumAsync(e => e.Amount, ct);
        if (total != 0)
        {
            problems.Add(Invariant($"The ledger entries add up to {total}, not 0."));
        }
    }

    private async Task CheckTheTransfersOf(DateOnly day, List<string> problems, CancellationToken ct)
    {
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = start.AddDays(1);

        var broken = await db.Transfers
            .Where(t => t.CreatedAt >= start && t.CreatedAt < end)
            .Select(t => new { t.Id, Count = t.Entries.Count, Sum = t.Entries.Sum(e => e.Amount) })
            .Where(t => t.Count != 2 || t.Sum != 0)
            .OrderBy(t => t.Id)
            .Take(MaxListed + 1)
            .ToListAsync(ct);

        problems.AddRange(broken.Take(MaxListed)
            .Select(t => Invariant($"Transfer {t.Id} has {t.Count} ledger entries that add up to {t.Sum}.")));
        if (broken.Count > MaxListed)
        {
            problems.Add("More transfers have the same problem.");
        }
    }

    private async Task CheckForCustomerAccountsBelowZero(List<string> problems, CancellationToken ct)
    {
        var belowZero = await db.LedgerEntries
            .Where(e => e.AccountId != Account.ExternalFundingId)
            .GroupBy(e => e.AccountId)
            .Select(g => new { AccountId = g.Key, Balance = g.Sum(e => e.Amount) })
            .Where(a => a.Balance < 0)
            .OrderBy(a => a.AccountId)
            .Take(MaxListed + 1)
            .ToListAsync(ct);

        problems.AddRange(belowZero.Take(MaxListed)
            .Select(a => Invariant($"Account {a.AccountId} has a balance of {a.Balance}.")));
        if (belowZero.Count > MaxListed)
        {
            problems.Add("More accounts have the same problem.");
        }
    }
}

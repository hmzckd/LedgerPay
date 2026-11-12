using LedgerPay.Api.Accounts;
using LedgerPay.Api.Data;
using Microsoft.EntityFrameworkCore;
using static System.FormattableString;

namespace LedgerPay.Api.Reconciliation;

public class Reconciler(LedgerDbContext db, TimeProvider clock, ILogger<Reconciler> logger)
{
    private const int MaxListed = 10;

    public async Task<ReconciliationRun> RunAsync(DateOnly day, CancellationToken ct)
    {
        var startedAt = clock.GetUtcNow();
        var problems = new List<string>();

        await CheckTheTotal(problems, ct);
        await CheckTheTransfersOf(day, problems, ct);
        await CheckForCustomerAccountsBelowZero(problems, ct);

        var run = ReconciliationRun.Finished(day, problems, startedAt, clock.GetUtcNow());
        db.ReconciliationRuns.Add(run);
        await db.SaveChangesAsync(ct);

        if (run.Status == ReconciliationStatus.Failed)
        {
            logger.LogError(
                "Reconciliation of {Day:O} failed with {Count} problems: {Problems}",
                day, problems.Count, string.Join(" ", problems));
        }
        else
        {
            logger.LogInformation("Reconciliation of {Day:O} passed.", day);
        }

        return run;
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

using LedgerPay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LedgerPay.Api.Ledger;

public static class Balances
{
    public static Task<long> BalanceOfAsync(this LedgerDbContext db, Guid accountId, CancellationToken ct) =>
        db.LedgerEntries.Where(e => e.AccountId == accountId).SumAsync(e => e.Amount, ct);
}

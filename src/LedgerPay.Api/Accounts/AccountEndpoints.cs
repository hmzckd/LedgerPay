using LedgerPay.Api.Data;
using LedgerPay.Api.Ledger;
using LedgerPay.Api.Transfers;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LedgerPay.Api.Accounts;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/api/accounts");
        accounts.MapPost("/", Open);
        accounts.MapGet("/{id:guid}", Get);
        accounts.MapGet("/{id:guid}/entries", ListEntries);
        accounts.MapPost("/{id:guid}/deposits", Deposit);
    }

    private static async Task<Created<AccountResponse>> Open(
        OpenAccountRequest request, LedgerDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var account = Account.Open(request.OwnerName.Trim(), clock);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/accounts/{account.Id}", AccountResponse.From(account, 0));
    }

    private static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> Get(
        Guid id, LedgerDbContext db, CancellationToken ct)
    {
        var account = await db.Accounts.FindAsync([id], ct);
        if (account is null)
        {
            return Problems.AccountNotFound(id);
        }

        var balance = await db.BalanceOfAsync(id, ct);
        return TypedResults.Ok(AccountResponse.From(account, balance));
    }

    private static async Task<Results<Ok<List<LedgerEntryResponse>>, ProblemHttpResult>> ListEntries(
        Guid id, LedgerDbContext db, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == id, ct))
        {
            return Problems.AccountNotFound(id);
        }

        var entries = await db.LedgerEntries
            .Where(e => e.AccountId == id)
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Take(50)
            .Select(e => new LedgerEntryResponse(e.Id, e.TransferId, e.Amount, e.CreatedAt))
            .ToListAsync(ct);

        return TypedResults.Ok(entries);
    }

    private static async Task<Results<Created<TransferResponse>, ProblemHttpResult>> Deposit(
        Guid id, DepositRequest request, LedgerDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (id == Account.ExternalFundingId)
        {
            return Problems.SameAccount();
        }

        if (!await db.Accounts.AnyAsync(a => a.Id == id, ct))
        {
            return Problems.AccountNotFound(id);
        }

        var transfer = Transfer.Create(Account.ExternalFundingId, id, request.Amount, clock);
        db.Transfers.Add(transfer);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/transfers/{transfer.Id}", TransferResponse.From(transfer));
    }
}

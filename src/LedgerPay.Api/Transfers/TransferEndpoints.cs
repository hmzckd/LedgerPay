using LedgerPay.Api.Accounts;
using LedgerPay.Api.Data;
using LedgerPay.Api.Ledger;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LedgerPay.Api.Transfers;

public static class TransferEndpoints
{
    public static void MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var transfers = app.MapGroup("/api/transfers");
        transfers.MapPost("/", Send);
        transfers.MapGet("/{id:guid}", Get);
    }

    private static async Task<Results<Created<TransferResponse>, ProblemHttpResult>> Send(
        TransferRequest request, LedgerDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (request.FromAccountId == request.ToAccountId)
        {
            return Problems.SameAccount();
        }

        if (request.FromAccountId == Account.ExternalFundingId || request.ToAccountId == Account.ExternalFundingId)
        {
            return Problems.ExternalFundingNotAllowed();
        }

        foreach (var accountId in new[] { request.FromAccountId, request.ToAccountId })
        {
            if (!await db.Accounts.AnyAsync(a => a.Id == accountId, ct))
            {
                return Problems.AccountNotFound(accountId);
            }
        }

        var balance = await db.BalanceOfAsync(request.FromAccountId, ct);
        if (balance < request.Amount)
        {
            return Problems.InsufficientFunds(request.FromAccountId);
        }

        var transfer = Transfer.Create(request.FromAccountId, request.ToAccountId, request.Amount, clock);
        db.Transfers.Add(transfer);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/transfers/{transfer.Id}", TransferResponse.From(transfer));
    }

    private static async Task<Results<Ok<TransferResponse>, ProblemHttpResult>> Get(
        Guid id, LedgerDbContext db, CancellationToken ct)
    {
        var transfer = await db.Transfers.FindAsync([id], ct);
        if (transfer is null)
        {
            return Problems.TransferNotFound(id);
        }

        return TypedResults.Ok(TransferResponse.From(transfer));
    }
}

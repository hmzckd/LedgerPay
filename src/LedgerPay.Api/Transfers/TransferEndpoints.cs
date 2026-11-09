using LedgerPay.Api.Accounts;
using LedgerPay.Api.Data;
using LedgerPay.Api.Ledger;
using LedgerPay.Api.Outbox;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
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
        TransferRequest request,
        [FromHeader(Name = Idempotency.KeyHeader)] string? idempotencyKey,
        LedgerDbContext db, TimeProvider clock, HttpResponse response, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > Idempotency.MaxKeyLength)
        {
            return Problems.InvalidIdempotencyKey();
        }

        var replay = await db.TryReplayAsync(
            idempotencyKey, request.FromAccountId, request.ToAccountId, request.Amount, response, ct);
        if (replay is not null)
        {
            return replay;
        }

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

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM accounts WHERE id = {request.FromAccountId} FOR NO KEY UPDATE", ct);

        var transfer = Transfer.Create(
            request.FromAccountId, request.ToAccountId, request.Amount, idempotencyKey, clock);
        db.Transfers.Add(transfer);
        db.OutboxMessages.Add(OutboxMessage.ForCompletedTransfer(transfer));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            await transaction.RollbackAsync(ct);
            return (await db.TryReplayAsync(
                idempotencyKey, request.FromAccountId, request.ToAccountId, request.Amount, response, ct))!;
        }

        var balance = await db.BalanceOfAsync(request.FromAccountId, ct);
        if (balance < 0)
        {
            return Problems.InsufficientFunds(request.FromAccountId);
        }

        await transaction.CommitAsync(ct);

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

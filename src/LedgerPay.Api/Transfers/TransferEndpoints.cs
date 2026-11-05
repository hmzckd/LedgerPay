using LedgerPay.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LedgerPay.Api.Transfers;

public static class TransferEndpoints
{
    public static void MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var transfers = app.MapGroup("/api/transfers");
        transfers.MapGet("/{id:guid}", Get);
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

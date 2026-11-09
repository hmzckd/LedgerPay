using LedgerPay.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LedgerPay.Api.Transfers;

public static class Idempotency
{
    public const string KeyHeader = "Idempotency-Key";
    public const int MaxKeyLength = 255;

    public static async Task<Results<Created<TransferResponse>, ProblemHttpResult>?> TryReplayAsync(
        this LedgerDbContext db, string key, Guid fromAccountId, Guid toAccountId, long amount,
        HttpResponse response, CancellationToken ct)
    {
        var existing = await db.Transfers.AsNoTracking().SingleOrDefaultAsync(t => t.IdempotencyKey == key, ct);
        if (existing is null)
        {
            return null;
        }

        if (!existing.IsFor(fromAccountId, toAccountId, amount))
        {
            return Problems.IdempotencyKeyReused();
        }

        response.Headers["Idempotent-Replayed"] = "true";
        return TypedResults.Created($"/api/transfers/{existing.Id}", TransferResponse.From(existing));
    }

    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

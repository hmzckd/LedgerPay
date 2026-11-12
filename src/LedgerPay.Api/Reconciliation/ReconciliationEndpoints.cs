using LedgerPay.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LedgerPay.Api.Reconciliation;

public static class ReconciliationEndpoints
{
    public static void MapReconciliationEndpoints(this IEndpointRouteBuilder app)
    {
        var runs = app.MapGroup("/api/reconciliation-runs");
        runs.MapPost("/", Run);
        runs.MapGet("/latest", GetLatest);
    }

    private static async Task<Results<Ok<ReconciliationRunResponse>, ProblemHttpResult>> Run(
        RunReconciliationRequest? request, Reconciler reconciler, TimeProvider clock, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var day = request?.Day ?? today.AddDays(-1);
        if (day > today)
        {
            return Problems.ReconciliationDayInTheFuture(day);
        }

        var run = await reconciler.RunManuallyAsync(day, ct);
        return TypedResults.Ok(ReconciliationRunResponse.From(run));
    }

    private static async Task<Results<Ok<ReconciliationRunResponse>, ProblemHttpResult>> GetLatest(
        LedgerDbContext db, CancellationToken ct)
    {
        var run = await db.ReconciliationRuns
            .OrderByDescending(r => r.StartedAt)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);
        if (run is null)
        {
            return Problems.NoReconciliationRun();
        }

        return TypedResults.Ok(ReconciliationRunResponse.From(run));
    }
}

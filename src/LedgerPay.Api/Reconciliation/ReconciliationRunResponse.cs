namespace LedgerPay.Api.Reconciliation;

public record ReconciliationRunResponse(
    Guid Id, DateOnly Day, string Status, List<string> Problems, DateTimeOffset StartedAt, DateTimeOffset FinishedAt)
{
    public static ReconciliationRunResponse From(ReconciliationRun run) =>
        new(run.Id, run.Day, run.Status.ToString(), run.Problems, run.StartedAt, run.FinishedAt);
}

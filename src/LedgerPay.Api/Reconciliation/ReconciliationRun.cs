namespace LedgerPay.Api.Reconciliation;

public enum ReconciliationStatus
{
    Passed,
    Failed,
}

public class ReconciliationRun
{
    public Guid Id { get; private set; }
    public DateOnly Day { get; private set; }
    public ReconciliationStatus Status { get; private set; }
    public List<string> Problems { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset FinishedAt { get; private set; }

    private ReconciliationRun(
        Guid id, DateOnly day, ReconciliationStatus status, List<string> problems,
        DateTimeOffset startedAt, DateTimeOffset finishedAt)
    {
        Id = id;
        Day = day;
        Status = status;
        Problems = problems;
        StartedAt = startedAt;
        FinishedAt = finishedAt;
    }

    public static ReconciliationRun Finished(
        DateOnly day, List<string> problems, DateTimeOffset startedAt, DateTimeOffset finishedAt) =>
        new(
            Guid.CreateVersion7(), day,
            problems.Count == 0 ? ReconciliationStatus.Passed : ReconciliationStatus.Failed,
            problems, startedAt, finishedAt);
}

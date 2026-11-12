namespace LedgerPay.Api.Reconciliation;

public enum ReconciliationKind
{
    Manual,
    Scheduled,
}

public enum ReconciliationStatus
{
    Running,
    Passed,
    Failed,
}

public class ReconciliationRun
{
    public Guid Id { get; private set; }
    public DateOnly Day { get; private set; }
    public ReconciliationKind Kind { get; private set; }
    public ReconciliationStatus Status { get; private set; }
    public List<string> Problems { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    private ReconciliationRun(Guid id, DateOnly day, ReconciliationKind kind, DateTimeOffset startedAt)
    {
        Id = id;
        Day = day;
        Kind = kind;
        Status = ReconciliationStatus.Running;
        Problems = [];
        StartedAt = startedAt;
    }

    public static ReconciliationRun Start(DateOnly day, ReconciliationKind kind, DateTimeOffset startedAt) =>
        new(Guid.CreateVersion7(), day, kind, startedAt);

    public void Finish(List<string> problems, DateTimeOffset finishedAt)
    {
        if (Status != ReconciliationStatus.Running)
        {
            throw new InvalidOperationException("This reconciliation run has already finished.");
        }

        Problems = problems;
        Status = problems.Count == 0 ? ReconciliationStatus.Passed : ReconciliationStatus.Failed;
        FinishedAt = finishedAt;
    }
}

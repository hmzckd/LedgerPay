using System.Diagnostics;
using System.Text.Json;
using LedgerPay.Api.Transfers;

namespace LedgerPay.Api.Outbox;

public class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Type { get; private set; }
    public string Payload { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public string? TraceParent { get; private set; }

    private OutboxMessage(Guid id, string type, string payload, DateTimeOffset createdAt, string? traceParent)
    {
        Id = id;
        Type = type;
        Payload = payload;
        CreatedAt = createdAt;
        TraceParent = traceParent;
    }

    public static OutboxMessage ForCompletedTransfer(Transfer transfer)
    {
        var id = Guid.CreateVersion7();
        var completed = new TransferCompleted(
            id, transfer.Id, transfer.FromAccountId, transfer.ToAccountId, transfer.Amount, transfer.CreatedAt);
        var payload = JsonSerializer.Serialize(completed, JsonSerializerOptions.Web);
        return new OutboxMessage(id, TransferCompleted.Type, payload, transfer.CreatedAt, Activity.Current?.Id);
    }

    public void MarkSent(TimeProvider clock)
    {
        Attempts++;
        SentAt = clock.GetUtcNow();
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error;
    }
}

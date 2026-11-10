using System.Globalization;

namespace LedgerPay.Notifications.Notifications;

public class Notification
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid TransferId { get; private set; }
    public Guid FromAccountId { get; private set; }
    public long Amount { get; private set; }
    public string Message { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Notification(
        Guid id, Guid eventId, Guid accountId, Guid transferId, Guid fromAccountId, long amount, string message,
        DateTimeOffset occurredAt, DateTimeOffset createdAt)
    {
        Id = id;
        EventId = eventId;
        AccountId = accountId;
        TransferId = transferId;
        FromAccountId = fromAccountId;
        Amount = amount;
        Message = message;
        OccurredAt = occurredAt;
        CreatedAt = createdAt;
    }

    public static Notification For(TransferCompleted completed, TimeProvider clock)
    {
        var message = string.Create(CultureInfo.InvariantCulture, $"You received {completed.Amount / 100m:0.00} TRY.");
        return new Notification(
            Guid.CreateVersion7(), completed.EventId, completed.ToAccountId, completed.TransferId,
            completed.FromAccountId, completed.Amount, message, completed.OccurredAt, clock.GetUtcNow());
    }
}

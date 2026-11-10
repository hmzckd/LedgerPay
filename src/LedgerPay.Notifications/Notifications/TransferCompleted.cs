namespace LedgerPay.Notifications.Notifications;

public record TransferCompleted(
    Guid EventId, Guid TransferId, Guid FromAccountId, Guid ToAccountId, long Amount, DateTimeOffset OccurredAt);

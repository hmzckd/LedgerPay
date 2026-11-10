namespace LedgerPay.Notifications.Notifications;

public record NotificationResponse(
    Guid Id, Guid TransferId, Guid FromAccountId, long Amount, string Message, DateTimeOffset OccurredAt);

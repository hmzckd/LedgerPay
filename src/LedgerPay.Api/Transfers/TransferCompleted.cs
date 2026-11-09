namespace LedgerPay.Api.Transfers;

public record TransferCompleted(
    Guid EventId, Guid TransferId, Guid FromAccountId, Guid ToAccountId, long Amount, DateTimeOffset OccurredAt)
{
    public const string Type = "transfer.completed";
}

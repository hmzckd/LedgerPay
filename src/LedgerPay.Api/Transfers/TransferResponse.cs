namespace LedgerPay.Api.Transfers;

public record TransferResponse(Guid Id, Guid FromAccountId, Guid ToAccountId, long Amount, DateTimeOffset CreatedAt)
{
    public static TransferResponse From(Transfer transfer) =>
        new(transfer.Id, transfer.FromAccountId, transfer.ToAccountId, transfer.Amount, transfer.CreatedAt);
}

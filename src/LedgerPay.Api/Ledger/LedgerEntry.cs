namespace LedgerPay.Api.Ledger;

public class LedgerEntry
{
    public Guid Id { get; private set; }
    public Guid TransferId { get; private set; }
    public Guid AccountId { get; private set; }
    public long Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public LedgerEntry(Guid id, Guid transferId, Guid accountId, long amount, DateTimeOffset createdAt)
    {
        Id = id;
        TransferId = transferId;
        AccountId = accountId;
        Amount = amount;
        CreatedAt = createdAt;
    }
}

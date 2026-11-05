using LedgerPay.Api.Ledger;

namespace LedgerPay.Api.Transfers;

public class Transfer
{
    private readonly List<LedgerEntry> _entries = [];

    public Guid Id { get; private set; }
    public Guid FromAccountId { get; private set; }
    public Guid ToAccountId { get; private set; }
    public long Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<LedgerEntry> Entries => _entries;

    private Transfer(Guid id, Guid fromAccountId, Guid toAccountId, long amount, DateTimeOffset createdAt)
    {
        Id = id;
        FromAccountId = fromAccountId;
        ToAccountId = toAccountId;
        Amount = amount;
        CreatedAt = createdAt;
    }

    public static Transfer Create(Guid fromAccountId, Guid toAccountId, long amount, TimeProvider clock)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        if (fromAccountId == toAccountId)
        {
            throw new ArgumentException("The sender and the receiver must be different accounts.");
        }

        var transfer = new Transfer(Guid.CreateVersion7(), fromAccountId, toAccountId, amount, clock.GetUtcNow());
        transfer._entries.Add(new LedgerEntry(Guid.CreateVersion7(), transfer.Id, fromAccountId, -amount, transfer.CreatedAt));
        transfer._entries.Add(new LedgerEntry(Guid.CreateVersion7(), transfer.Id, toAccountId, amount, transfer.CreatedAt));
        return transfer;
    }
}

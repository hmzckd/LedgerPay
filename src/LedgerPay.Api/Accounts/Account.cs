namespace LedgerPay.Api.Accounts;

public class Account
{
    public static readonly Guid ExternalFundingId = new("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; private set; }
    public string OwnerName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public Account(Guid id, string ownerName, DateTimeOffset createdAt)
    {
        Id = id;
        OwnerName = ownerName;
        CreatedAt = createdAt;
    }

    public static Account Open(string ownerName, TimeProvider clock) =>
        new(Guid.CreateVersion7(), ownerName, clock.GetUtcNow());
}

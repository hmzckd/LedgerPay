namespace LedgerPay.Api.Accounts;

public record AccountResponse(Guid Id, string OwnerName, long Balance, DateTimeOffset CreatedAt)
{
    public static AccountResponse From(Account account) =>
        new(account.Id, account.OwnerName, 0, account.CreatedAt);
}

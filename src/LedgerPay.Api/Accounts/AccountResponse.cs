namespace LedgerPay.Api.Accounts;

public record AccountResponse(Guid Id, string OwnerName, long Balance, DateTimeOffset CreatedAt)
{
    public static AccountResponse From(Account account, long balance) =>
        new(account.Id, account.OwnerName, balance, account.CreatedAt);
}

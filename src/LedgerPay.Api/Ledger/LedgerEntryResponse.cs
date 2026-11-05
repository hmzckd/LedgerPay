namespace LedgerPay.Api.Ledger;

public record LedgerEntryResponse(Guid Id, Guid TransferId, long Amount, DateTimeOffset CreatedAt);

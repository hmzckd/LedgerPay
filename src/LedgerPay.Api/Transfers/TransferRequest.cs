using System.ComponentModel.DataAnnotations;

namespace LedgerPay.Api.Transfers;

public record TransferRequest(Guid FromAccountId, Guid ToAccountId, [property: Range(1, 1_000_000_000)] long Amount);

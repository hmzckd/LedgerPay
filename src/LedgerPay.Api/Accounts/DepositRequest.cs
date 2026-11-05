using System.ComponentModel.DataAnnotations;

namespace LedgerPay.Api.Accounts;

public record DepositRequest([property: Range(1, 1_000_000_000)] long Amount);

using System.ComponentModel.DataAnnotations;

namespace LedgerPay.Api.Accounts;

public record OpenAccountRequest([property: Required, StringLength(200)] string OwnerName);

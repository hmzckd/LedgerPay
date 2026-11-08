using Microsoft.AspNetCore.Http.HttpResults;

namespace LedgerPay.Api;

public static class Problems
{
    public static ProblemHttpResult AccountNotFound(Guid id) =>
        TypedResults.Problem(
            title: "Account not found",
            detail: $"There is no account with id {id}.",
            statusCode: StatusCodes.Status404NotFound);

    public static ProblemHttpResult TransferNotFound(Guid id) =>
        TypedResults.Problem(
            title: "Transfer not found",
            detail: $"There is no transfer with id {id}.",
            statusCode: StatusCodes.Status404NotFound);

    public static ProblemHttpResult SameAccount() =>
        TypedResults.Problem(
            title: "Cannot transfer to the same account",
            detail: "The sender and the receiver must be different accounts.",
            statusCode: StatusCodes.Status400BadRequest);

    public static ProblemHttpResult ExternalFundingNotAllowed() =>
        TypedResults.Problem(
            title: "External funding account not allowed",
            detail: "Money enters LedgerPay only through deposits, so transfers cannot use the external funding account.",
            statusCode: StatusCodes.Status400BadRequest);

    public static ProblemHttpResult InsufficientFunds(Guid accountId) =>
        TypedResults.Problem(
            type: "insufficient-funds",
            title: "Insufficient funds",
            detail: $"The balance of account {accountId} is lower than the amount.",
            statusCode: StatusCodes.Status409Conflict);
}

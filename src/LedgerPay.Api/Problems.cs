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

    public static ProblemHttpResult InvalidIdempotencyKey() =>
        TypedResults.Problem(
            title: "Invalid Idempotency-Key header",
            detail: "Every deposit and transfer needs an Idempotency-Key header of 1 to 255 characters.",
            statusCode: StatusCodes.Status400BadRequest);

    public static ProblemHttpResult IdempotencyKeyReused() =>
        TypedResults.Problem(
            type: "idempotency-key-reused",
            title: "Idempotency key already used",
            detail: "This Idempotency-Key was already used for a request with other accounts or another amount.",
            statusCode: StatusCodes.Status422UnprocessableEntity);

    public static ProblemHttpResult RiskRejected(string reason) =>
        TypedResults.Problem(
            type: "risk-rejected",
            title: "Rejected by the risk check",
            detail: reason,
            statusCode: StatusCodes.Status422UnprocessableEntity);

    public static ProblemHttpResult RiskUnavailable() =>
        TypedResults.Problem(
            type: "risk-unavailable",
            title: "Risk check unavailable",
            detail: "The risk check gave no answer, so no money moved. Try again later with the same Idempotency-Key.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
}

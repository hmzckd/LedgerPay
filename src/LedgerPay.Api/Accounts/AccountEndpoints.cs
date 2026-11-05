using LedgerPay.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LedgerPay.Api.Accounts;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/api/accounts");
        accounts.MapPost("/", Open);
        accounts.MapGet("/{id:guid}", Get);
    }

    private static async Task<Created<AccountResponse>> Open(
        OpenAccountRequest request, LedgerDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var account = Account.Open(request.OwnerName.Trim(), clock);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/accounts/{account.Id}", AccountResponse.From(account));
    }

    private static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> Get(
        Guid id, LedgerDbContext db, CancellationToken ct)
    {
        var account = await db.Accounts.FindAsync([id], ct);
        if (account is null)
        {
            return TypedResults.Problem(
                title: "Account not found",
                detail: $"There is no account with id {id}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(AccountResponse.From(account));
    }
}

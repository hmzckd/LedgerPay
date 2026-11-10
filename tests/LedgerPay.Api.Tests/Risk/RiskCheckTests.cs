using System.Net;
using System.Net.Http.Json;
using Grpc.Core;
using LedgerPay.Api.Accounts;
using LedgerPay.Risk.V1;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace LedgerPay.Api.Tests.Risk;

public class RiskCheckTests(LedgerPayApiFactory factory)
{
    private const string OverTheLimit = "The amount is over the 50000.00 TRY limit.";

    private readonly HttpClient _client = factory.CreateClient();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_transfer_is_checked_with_a_one_second_deadline()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);

        var before = DateTime.UtcNow;
        var response = await PostTransfer(ali.Id, ayse.Id, 4000);
        var after = DateTime.UtcNow;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var call = Assert.Single(factory.Risk.CallsFor(ayse.Id));
        Assert.Equal(ali.Id.ToString(), call.Request.FromAccountId);
        Assert.Equal(4000, call.Request.Amount);
        Assert.NotNull(call.Deadline);
        Assert.InRange(call.Deadline.Value, before.AddSeconds(1), after.AddSeconds(1));
    }

    [Fact]
    public async Task Every_deposit_is_checked_as_money_from_the_external_funding_account()
    {
        var ali = await OpenAccountWith(0);

        var response = await PostDeposit(ali.Id, 2500);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var call = Assert.Single(factory.Risk.CallsFor(ali.Id));
        Assert.Equal(Account.ExternalFundingId.ToString(), call.Request.FromAccountId);
        Assert.Equal(2500, call.Request.Amount);
    }

    [Fact]
    public async Task A_transfer_the_risk_check_rejects_returns_422_and_moves_no_money()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);
        factory.Risk.RejectFor(ayse.Id, OverTheLimit);

        var response = await PostTransfer(ali.Id, ayse.Id, 4000);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_ct);
        Assert.Equal("risk-rejected", problem!.Type);
        Assert.Equal(OverTheLimit, problem.Detail);
        Assert.Equal(10000, await BalanceOf(ali.Id));
        Assert.Equal(0, await BalanceOf(ayse.Id));
    }

    [Fact]
    public async Task A_deposit_the_risk_check_rejects_returns_422_and_moves_no_money()
    {
        var ali = await OpenAccountWith(0);
        factory.Risk.RejectFor(ali.Id, OverTheLimit);

        var response = await PostDeposit(ali.Id, 6_000_000);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0, await BalanceOf(ali.Id));
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.Internal)]
    public async Task A_transfer_the_risk_check_cannot_answer_returns_503_and_moves_no_money(StatusCode status)
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);
        factory.Risk.FailFor(ayse.Id, status);

        var response = await PostTransfer(ali.Id, ayse.Id, 4000);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_ct);
        Assert.Equal("risk-unavailable", problem!.Type);
        Assert.Equal(10000, await BalanceOf(ali.Id));
        Assert.Equal(0, await BalanceOf(ayse.Id));
    }

    [Fact]
    public async Task An_answer_without_a_decision_returns_503()
    {
        var ali = await OpenAccountWith(0);
        factory.Risk.AnswerFor(ali.Id, _ => new CheckTransferResponse());

        var response = await PostDeposit(ali.Id, 2500);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, await BalanceOf(ali.Id));
    }

    [Fact]
    public async Task A_repeated_request_does_not_ask_the_risk_check_again()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);
        var key = Guid.NewGuid().ToString();

        await PostTransfer(ali.Id, ayse.Id, 4000, key);
        var second = await PostTransfer(ali.Id, ayse.Id, 4000, key);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Single(factory.Risk.CallsFor(ayse.Id));
    }

    [Fact]
    public async Task A_rejected_request_can_be_sent_again_with_the_same_key()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);
        var key = Guid.NewGuid().ToString();
        factory.Risk.FailFor(ayse.Id, StatusCode.Unavailable);

        var first = await PostTransfer(ali.Id, ayse.Id, 4000, key);
        factory.Risk.ApproveFor(ayse.Id);
        var second = await PostTransfer(ali.Id, ayse.Id, 4000, key);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.False(second.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(4000, await BalanceOf(ayse.Id));
    }

    [Fact]
    public async Task A_transfer_to_an_unknown_account_does_not_ask_the_risk_check()
    {
        var ali = await OpenAccountWith(10000);
        var unknown = Guid.CreateVersion7();

        var response = await PostTransfer(ali.Id, unknown, 4000);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.Risk.CallsFor(unknown));
    }

    private async Task<AccountResponse> OpenAccountWith(long balance)
    {
        var response = await _client.PostAsJsonAsync("/api/accounts", new { ownerName = "Test Owner" }, _ct);
        response.EnsureSuccessStatusCode();
        var account = (await response.Content.ReadFromJsonAsync<AccountResponse>(_ct))!;
        if (balance > 0)
        {
            (await PostDeposit(account.Id, balance)).EnsureSuccessStatusCode();
        }

        return account;
    }

    private Task<HttpResponseMessage> PostTransfer(Guid from, Guid to, long amount, string? key = null) =>
        _client.PostWithKeyAsync("/api/transfers", new { fromAccountId = from, toAccountId = to, amount }, _ct, key);

    private Task<HttpResponseMessage> PostDeposit(Guid accountId, long amount) =>
        _client.PostWithKeyAsync($"/api/accounts/{accountId}/deposits", new { amount }, _ct);

    private async Task<long> BalanceOf(Guid id) =>
        (await _client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{id}", _ct))!.Balance;
}

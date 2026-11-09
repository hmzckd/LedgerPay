using System.Net;
using System.Net.Http.Json;
using LedgerPay.Api.Accounts;
using LedgerPay.Api.Transfers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace LedgerPay.Api.Tests.Transfers;

public class IdempotencyTests(LedgerPayApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_transfer_without_an_idempotency_key_returns_400()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);

        var response = await _client.PostAsJsonAsync(
            "/api/transfers", new { fromAccountId = ali.Id, toAccountId = ayse.Id, amount = 4000 }, _ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(10000, await BalanceOf(ali.Id));
    }

    [Fact]
    public async Task A_deposit_without_an_idempotency_key_returns_400()
    {
        var ali = await OpenAccountWith(0);

        var response = await _client.PostAsJsonAsync($"/api/accounts/{ali.Id}/deposits", new { amount = 4000 }, _ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await BalanceOf(ali.Id));
    }

    [Theory]
    [InlineData(255, HttpStatusCode.Created)]
    [InlineData(256, HttpStatusCode.BadRequest)]
    public async Task An_idempotency_key_can_be_at_most_255_characters(int length, HttpStatusCode expected)
    {
        var ali = await OpenAccountWith(0);
        var key = (Guid.NewGuid() + new string('k', 300))[..length];

        var response = await PostDeposit(ali.Id, 4000, key);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Sending_the_same_transfer_twice_moves_the_money_once()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);
        var key = Guid.NewGuid().ToString();

        var first = await PostTransfer(ali.Id, ayse.Id, 4000, key);
        var second = await PostTransfer(ali.Id, ayse.Id, 4000, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.False(IsReplayed(first));
        Assert.True(IsReplayed(second));
        Assert.Equal(await TransferIdOf(first), await TransferIdOf(second));
        Assert.Equal(6000, await BalanceOf(ali.Id));
        Assert.Equal(4000, await BalanceOf(ayse.Id));
    }

    [Fact]
    public async Task Sending_the_same_deposit_twice_deposits_once()
    {
        var ali = await OpenAccountWith(0);
        var key = Guid.NewGuid().ToString();

        var first = await PostDeposit(ali.Id, 4000, key);
        var second = await PostDeposit(ali.Id, 4000, key);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.True(IsReplayed(second));
        Assert.Equal(await TransferIdOf(first), await TransferIdOf(second));
        Assert.Equal(4000, await BalanceOf(ali.Id));
    }

    [Fact]
    public async Task Reusing_a_key_with_a_different_amount_returns_422_and_moves_nothing()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);
        var key = Guid.NewGuid().ToString();
        await PostTransfer(ali.Id, ayse.Id, 4000, key);

        var response = await PostTransfer(ali.Id, ayse.Id, 5000, key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_ct);
        Assert.Equal("idempotency-key-reused", problem?.Type);
        Assert.Equal(6000, await BalanceOf(ali.Id));
    }

    [Fact]
    public async Task A_key_rejected_with_409_can_be_used_again()
    {
        var ali = await OpenAccountWith(5000);
        var ayse = await OpenAccountWith(0);
        var key = Guid.NewGuid().ToString();

        var rejected = await PostTransfer(ali.Id, ayse.Id, 8000, key);
        var accepted = await PostTransfer(ali.Id, ayse.Id, 3000, key);
        var replayed = await PostTransfer(ali.Id, ayse.Id, 3000, key);

        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.False(IsReplayed(accepted));
        Assert.True(IsReplayed(replayed));
        Assert.Equal(2000, await BalanceOf(ali.Id));
    }

    [Fact]
    public async Task Ten_identical_transfers_at_the_same_time_move_the_money_once()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PostTransfer(ali.Id, ayse.Id, 8000, key)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Single(responses, r => !IsReplayed(r));
        Assert.Single((await Task.WhenAll(responses.Select(TransferIdOf))).Distinct());
        Assert.Equal(2000, await BalanceOf(ali.Id));
    }

    [Fact]
    public async Task Ten_identical_deposits_at_the_same_time_deposit_once()
    {
        var ali = await OpenAccountWith(0);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PostDeposit(ali.Id, 4000, key)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Single(responses, r => !IsReplayed(r));
        Assert.Equal(4000, await BalanceOf(ali.Id));
    }

    private async Task<AccountResponse> OpenAccountWith(long balance)
    {
        var response = await _client.PostAsJsonAsync("/api/accounts", new { ownerName = "Test Owner" }, _ct);
        response.EnsureSuccessStatusCode();
        var account = (await response.Content.ReadFromJsonAsync<AccountResponse>(_ct))!;
        if (balance > 0)
        {
            (await PostDeposit(account.Id, balance, Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        }

        return account;
    }

    private Task<HttpResponseMessage> PostTransfer(Guid from, Guid to, long amount, string key) =>
        _client.PostWithKeyAsync("/api/transfers", new { fromAccountId = from, toAccountId = to, amount }, _ct, key);

    private Task<HttpResponseMessage> PostDeposit(Guid accountId, long amount, string key) =>
        _client.PostWithKeyAsync($"/api/accounts/{accountId}/deposits", new { amount }, _ct, key);

    private static bool IsReplayed(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Idempotent-Replayed", out var values) && values.Single() == "true";

    private async Task<Guid> TransferIdOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<TransferResponse>(_ct))!.Id;

    private async Task<long> BalanceOf(Guid id) =>
        (await _client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{id}", _ct))!.Balance;
}

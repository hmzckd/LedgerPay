using System.Net;
using System.Net.Http.Json;
using LedgerPay.Api.Accounts;
using LedgerPay.Api.Ledger;
using LedgerPay.Api.Transfers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace LedgerPay.Api.Tests.Transfers;

public class TransferTests(LedgerPayApiFactory factory)
{
    private const string ExternalFundingId = "00000000-0000-0000-0000-000000000001";

    private readonly HttpClient _client = factory.CreateClient();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_transfer_returns_201_and_moves_the_money()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);

        var response = await PostTransfer(ali.Id, ayse.Id, 4000);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transfer = await response.Content.ReadFromJsonAsync<TransferResponse>(_ct);
        Assert.NotNull(transfer);
        Assert.Equal(ali.Id, transfer.FromAccountId);
        Assert.Equal(ayse.Id, transfer.ToAccountId);
        Assert.Equal(4000, transfer.Amount);
        Assert.Equal($"/api/transfers/{transfer.Id}", response.Headers.Location?.ToString());
        Assert.Equal(6000, await BalanceOf(ali.Id));
        Assert.Equal(4000, await BalanceOf(ayse.Id));
    }

    [Fact]
    public async Task A_transfer_writes_a_minus_entry_for_the_sender_and_a_plus_entry_for_the_receiver()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);

        var transfer = await Transfer(ali.Id, ayse.Id, 4000);

        var aliEntry = (await GetEntries(ali.Id))[0];
        var ayseEntry = Assert.Single(await GetEntries(ayse.Id));
        Assert.Equal((transfer.Id, -4000L), (aliEntry.TransferId, aliEntry.Amount));
        Assert.Equal((transfer.Id, 4000L), (ayseEntry.TransferId, ayseEntry.Amount));
    }

    [Fact]
    public async Task The_whole_balance_can_be_sent()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);

        var response = await PostTransfer(ali.Id, ayse.Id, 10000);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(0, await BalanceOf(ali.Id));
    }

    [Fact]
    public async Task Sending_more_than_the_balance_returns_409_and_moves_nothing()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);

        var response = await PostTransfer(ali.Id, ayse.Id, 10001);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_ct);
        Assert.Equal("insufficient-funds", problem?.Type);
        Assert.Equal("Insufficient funds", problem?.Title);
        Assert.Equal(10000, await BalanceOf(ali.Id));
        Assert.Equal(0, await BalanceOf(ayse.Id));
    }

    [Fact]
    public async Task An_account_without_money_cannot_send_anything()
    {
        var ali = await OpenAccountWith(0);
        var ayse = await OpenAccountWith(0);

        var response = await PostTransfer(ali.Id, ayse.Id, 1);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_transfer_to_the_same_account_returns_400()
    {
        var ali = await OpenAccountWith(10000);

        var response = await PostTransfer(ali.Id, ali.Id, 100);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Cannot transfer to the same account", await ProblemTitle(response));
    }

    [Fact]
    public async Task The_external_funding_account_cannot_send_a_transfer()
    {
        var ayse = await OpenAccountWith(0);

        var response = await PostTransfer(Guid.Parse(ExternalFundingId), ayse.Id, 100);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("External funding account not allowed", await ProblemTitle(response));
        Assert.Equal(0, await BalanceOf(ayse.Id));
    }

    [Fact]
    public async Task The_external_funding_account_cannot_receive_a_transfer()
    {
        var ali = await OpenAccountWith(10000);

        var response = await PostTransfer(ali.Id, Guid.Parse(ExternalFundingId), 100);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("External funding account not allowed", await ProblemTitle(response));
    }

    [Fact]
    public async Task An_unknown_sender_returns_404()
    {
        var ayse = await OpenAccountWith(0);

        var response = await PostTransfer(Guid.CreateVersion7(), ayse.Id, 100);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Account not found", await ProblemTitle(response));
    }

    [Fact]
    public async Task An_unknown_receiver_returns_404()
    {
        var ali = await OpenAccountWith(10000);

        var response = await PostTransfer(ali.Id, Guid.CreateVersion7(), 100);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Account not found", await ProblemTitle(response));
        Assert.Equal(10000, await BalanceOf(ali.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_transfer_of_zero_or_less_returns_400(long amount)
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);

        var response = await PostTransfer(ali.Id, ayse.Id, amount);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_transfer_can_be_read_back()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);
        var sent = await Transfer(ali.Id, ayse.Id, 1234);

        var transfer = await _client.GetFromJsonAsync<TransferResponse>($"/api/transfers/{sent.Id}", _ct);

        Assert.Equal(sent.Id, transfer?.Id);
        Assert.Equal(1234, transfer?.Amount);
    }

    [Fact]
    public async Task Transfers_sent_at_the_same_time_cannot_spend_the_same_money_twice()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PostTransfer(ali.Id, ayse.Id, 8000)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(2000, await BalanceOf(ali.Id));
        Assert.Equal(8000, await BalanceOf(ayse.Id));
    }

    [Fact]
    public async Task Opposite_transfers_at_the_same_time_do_not_block_each_other()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(10000);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            i % 2 == 0 ? PostTransfer(ali.Id, ayse.Id, 100) : PostTransfer(ayse.Id, ali.Id, 100)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(10000, await BalanceOf(ali.Id));
        Assert.Equal(10000, await BalanceOf(ayse.Id));
    }

    private async Task<AccountResponse> OpenAccountWith(long balance)
    {
        var response = await _client.PostAsJsonAsync("/api/accounts", new { ownerName = "Test Owner" }, _ct);
        response.EnsureSuccessStatusCode();
        var account = (await response.Content.ReadFromJsonAsync<AccountResponse>(_ct))!;
        if (balance > 0)
        {
            var deposit = await _client.PostWithKeyAsync($"/api/accounts/{account.Id}/deposits", new { amount = balance }, _ct);
            deposit.EnsureSuccessStatusCode();
        }

        return account;
    }

    private Task<HttpResponseMessage> PostTransfer(Guid from, Guid to, long amount) =>
        _client.PostWithKeyAsync("/api/transfers", new { fromAccountId = from, toAccountId = to, amount }, _ct);

    private async Task<TransferResponse> Transfer(Guid from, Guid to, long amount)
    {
        var response = await PostTransfer(from, to, amount);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransferResponse>(_ct))!;
    }

    private async Task<long> BalanceOf(Guid id) =>
        (await _client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{id}", _ct))!.Balance;

    private async Task<List<LedgerEntryResponse>> GetEntries(Guid id) =>
        (await _client.GetFromJsonAsync<List<LedgerEntryResponse>>($"/api/accounts/{id}/entries", _ct))!;

    private async Task<string?> ProblemTitle(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(_ct))?.Title;
}

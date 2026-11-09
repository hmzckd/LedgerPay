using System.Net;
using System.Net.Http.Json;
using LedgerPay.Api.Accounts;
using LedgerPay.Api.Ledger;
using LedgerPay.Api.Transfers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace LedgerPay.Api.Tests.Ledger;

public class DepositTests(LedgerPayApiFactory factory)
{
    private const string ExternalFundingId = "00000000-0000-0000-0000-000000000001";

    private readonly HttpClient _client = factory.CreateClient();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_deposit_returns_201_with_a_transfer_from_the_external_funding_account()
    {
        var account = await OpenAccount();

        var response = await _client.PostWithKeyAsync($"/api/accounts/{account.Id}/deposits", new { amount = 10000 }, _ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transfer = await response.Content.ReadFromJsonAsync<TransferResponse>(_ct);
        Assert.NotNull(transfer);
        Assert.Equal(Guid.Parse(ExternalFundingId), transfer.FromAccountId);
        Assert.Equal(account.Id, transfer.ToAccountId);
        Assert.Equal(10000, transfer.Amount);
        Assert.Equal($"/api/transfers/{transfer.Id}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task The_balance_is_the_sum_of_all_deposits()
    {
        var account = await OpenAccount();

        await Deposit(account.Id, 10000);
        await Deposit(account.Id, 2550);

        var balance = (await GetAccount(account.Id)).Balance;
        Assert.Equal(12550, balance);
    }

    [Fact]
    public async Task A_deposit_writes_one_entry_on_each_side()
    {
        var account = await OpenAccount();

        var transfer = await Deposit(account.Id, 7500);

        var accountEntries = await GetEntries(account.Id.ToString());
        var entry = Assert.Single(accountEntries);
        Assert.Equal(transfer.Id, entry.TransferId);
        Assert.Equal(7500, entry.Amount);

        var fundingEntries = await GetEntries(ExternalFundingId);
        Assert.Contains(fundingEntries, e => e.TransferId == transfer.Id && e.Amount == -7500);
    }

    [Fact]
    public async Task The_external_funding_account_goes_below_zero()
    {
        var account = await OpenAccount();
        await Deposit(account.Id, 5000);

        var funding = await GetAccount(Guid.Parse(ExternalFundingId));

        Assert.True(funding.Balance <= -5000);
    }

    [Fact]
    public async Task Entries_are_listed_newest_first_and_at_most_50()
    {
        var account = await OpenAccount();
        for (var amount = 1; amount <= 51; amount++)
        {
            await Deposit(account.Id, amount);
        }

        var entries = await GetEntries(account.Id.ToString());

        Assert.Equal(50, entries.Count);
        Assert.Equal(51, entries[0].Amount);
        Assert.Equal(2, entries[^1].Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task A_deposit_of_zero_or_less_returns_400(long amount)
    {
        var account = await OpenAccount();

        var response = await _client.PostWithKeyAsync($"/api/accounts/{account.Id}/deposits", new { amount }, _ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await GetAccount(account.Id)).Balance);
    }

    [Fact]
    public async Task A_deposit_into_an_unknown_account_returns_404()
    {
        var response = await _client.PostWithKeyAsync(
            $"/api/accounts/{Guid.CreateVersion7()}/deposits", new { amount = 100 }, _ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Account not found", await ProblemTitle(response));
    }

    [Fact]
    public async Task A_deposit_into_the_external_funding_account_returns_400()
    {
        var response = await _client.PostWithKeyAsync(
            $"/api/accounts/{ExternalFundingId}/deposits", new { amount = 100 }, _ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Cannot transfer to the same account", await ProblemTitle(response));
    }

    [Fact]
    public async Task A_deposit_can_be_read_back_as_a_transfer()
    {
        var account = await OpenAccount();
        var deposit = await Deposit(account.Id, 300);

        var transfer = await _client.GetFromJsonAsync<TransferResponse>($"/api/transfers/{deposit.Id}", _ct);

        Assert.Equal(deposit.Id, transfer?.Id);
        Assert.Equal(300, transfer?.Amount);
    }

    [Fact]
    public async Task Reading_an_unknown_transfer_returns_404()
    {
        var response = await _client.GetAsync($"/api/transfers/{Guid.CreateVersion7()}", _ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Transfer not found", await ProblemTitle(response));
    }

    [Fact]
    public async Task Listing_the_entries_of_an_unknown_account_returns_404()
    {
        var response = await _client.GetAsync($"/api/accounts/{Guid.CreateVersion7()}/entries", _ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Account not found", await ProblemTitle(response));
    }

    private async Task<AccountResponse> OpenAccount()
    {
        var response = await _client.PostAsJsonAsync("/api/accounts", new { ownerName = "Ali Yilmaz" }, _ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>(_ct))!;
    }

    private async Task<TransferResponse> Deposit(Guid accountId, long amount)
    {
        var response = await _client.PostWithKeyAsync($"/api/accounts/{accountId}/deposits", new { amount }, _ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransferResponse>(_ct))!;
    }

    private async Task<string?> ProblemTitle(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(_ct))?.Title;

    private async Task<AccountResponse> GetAccount(Guid id) =>
        (await _client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{id}", _ct))!;

    private async Task<List<LedgerEntryResponse>> GetEntries(string accountId) =>
        (await _client.GetFromJsonAsync<List<LedgerEntryResponse>>($"/api/accounts/{accountId}/entries", _ct))!;
}

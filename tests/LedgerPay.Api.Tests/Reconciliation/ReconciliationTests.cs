using System.Net;
using System.Net.Http.Json;
using LedgerPay.Api.Accounts;
using LedgerPay.Api.Data;
using LedgerPay.Api.Reconciliation;
using LedgerPay.Api.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LedgerPay.Api.Tests.Reconciliation;

[CollectionDefinition(nameof(ReconciliationTests), DisableParallelization = true)]
public class ReconciliationTestsCollection;

[Collection(nameof(ReconciliationTests))]
public class ReconciliationTests(LedgerPayApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_healthy_ledger_passes()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);
        (await PostTransfer(ali.Id, ayse.Id, 4000)).EnsureSuccessStatusCode();

        var run = await RunFor(Today());

        Assert.Equal("Passed", run.Status);
        Assert.Empty(run.Problems);
        Assert.Equal(Today(), run.Day);
    }

    [Fact]
    public async Task An_extra_ledger_entry_fails_the_total_and_its_transfer()
    {
        var ali = await OpenAccountWith(0);
        var deposit = await Deposit(ali.Id, 2500);
        var extraId = Guid.CreateVersion7();
        await Sql($"""
            INSERT INTO ledger_entries (id, transfer_id, account_id, amount, created_at)
            VALUES ({extraId}, {deposit.Id}, {ali.Id}, 100, {DateTimeOffset.UtcNow})
            """);
        try
        {
            var run = await RunFor(Today());

            Assert.Equal("Failed", run.Status);
            Assert.Equal(
                [
                    "The ledger entries add up to 100, not 0.",
                    $"Transfer {deposit.Id} has 3 ledger entries that add up to 100.",
                ],
                run.Problems);
        }
        finally
        {
            await Sql($"DELETE FROM ledger_entries WHERE id = {extraId}");
        }
    }

    [Fact]
    public async Task A_transfer_without_ledger_entries_fails()
    {
        var ali = await OpenAccountWith(0);
        var transferId = await InsertTransferWithoutEntries(ali.Id, DateTimeOffset.UtcNow);
        try
        {
            var run = await RunFor(Today());

            Assert.Equal("Failed", run.Status);
            Assert.Equal([$"Transfer {transferId} has 0 ledger entries that add up to 0."], run.Problems);
        }
        finally
        {
            await Sql($"DELETE FROM transfers WHERE id = {transferId}");
        }
    }

    [Fact]
    public async Task A_customer_account_below_zero_fails()
    {
        var ali = await OpenAccountWith(0);
        var transferId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        await Sql($"""
            INSERT INTO transfers (id, from_account_id, to_account_id, amount, idempotency_key, created_at)
            VALUES ({transferId}, {ali.Id}, {Account.ExternalFundingId}, 500, {transferId.ToString()}, {now})
            """);
        await Sql($"""
            INSERT INTO ledger_entries (id, transfer_id, account_id, amount, created_at)
            VALUES ({Guid.CreateVersion7()}, {transferId}, {ali.Id}, -500, {now}),
                   ({Guid.CreateVersion7()}, {transferId}, {Account.ExternalFundingId}, 500, {now})
            """);
        try
        {
            var run = await RunFor(Today());

            Assert.Equal("Failed", run.Status);
            Assert.Equal([$"Account {ali.Id} has a balance of -500."], run.Problems);
        }
        finally
        {
            await Sql($"DELETE FROM ledger_entries WHERE transfer_id = {transferId}");
            await Sql($"DELETE FROM transfers WHERE id = {transferId}");
        }
    }

    [Fact]
    public async Task Only_the_transfers_of_the_checked_day_are_checked()
    {
        var ali = await OpenAccountWith(0);
        var transferId = await InsertTransferWithoutEntries(ali.Id, DateTimeOffset.UtcNow.AddDays(-1));
        try
        {
            var today = await RunFor(Today());
            var yesterday = await RunFor(Today().AddDays(-1));

            Assert.Equal("Passed", today.Status);
            Assert.Equal("Failed", yesterday.Status);
            Assert.Equal([$"Transfer {transferId} has 0 ledger entries that add up to 0."], yesterday.Problems);
        }
        finally
        {
            await Sql($"DELETE FROM transfers WHERE id = {transferId}");
        }
    }

    [Fact]
    public async Task At_most_ten_broken_transfers_are_listed()
    {
        var ali = await OpenAccountWith(0);
        var transferIds = new List<Guid>();
        for (var i = 0; i < 11; i++)
        {
            transferIds.Add(await InsertTransferWithoutEntries(ali.Id, DateTimeOffset.UtcNow));
        }

        try
        {
            var run = await RunFor(Today());

            Assert.Equal(11, run.Problems.Count);
            Assert.Equal("More transfers have the same problem.", run.Problems[^1]);
        }
        finally
        {
            foreach (var transferId in transferIds)
            {
                await Sql($"DELETE FROM transfers WHERE id = {transferId}");
            }
        }
    }

    [Fact]
    public async Task Without_a_day_yesterday_is_checked()
    {
        var response = await _client.PostAsync("/api/reconciliation-runs", null, _ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<ReconciliationRunResponse>(_ct);
        Assert.Equal(Today().AddDays(-1), run!.Day);
    }

    [Fact]
    public async Task A_day_in_the_future_returns_400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/reconciliation-runs", new { day = Today().AddDays(1) }, _ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_latest_run_is_the_newest_one()
    {
        await RunFor(Today());
        var newest = await RunFor(Today().AddDays(-1));

        var latest = await _client.GetFromJsonAsync<ReconciliationRunResponse>(
            "/api/reconciliation-runs/latest", _ct);

        Assert.Equal(newest.Id, latest!.Id);
        Assert.Equal(newest.Day, latest.Day);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<ReconciliationRunResponse> RunFor(DateOnly day)
    {
        var response = await _client.PostAsJsonAsync("/api/reconciliation-runs", new { day }, _ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReconciliationRunResponse>(_ct))!;
    }

    private async Task<Guid> InsertTransferWithoutEntries(Guid toAccountId, DateTimeOffset createdAt)
    {
        var transferId = Guid.CreateVersion7();
        await Sql($"""
            INSERT INTO transfers (id, from_account_id, to_account_id, amount, idempotency_key, created_at)
            VALUES ({transferId}, {Account.ExternalFundingId}, {toAccountId}, 700, {transferId.ToString()}, {createdAt})
            """);
        return transferId;
    }

    private async Task Sql(FormattableString sql)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        await db.Database.ExecuteSqlAsync(sql, _ct);
    }

    private async Task<AccountResponse> OpenAccountWith(long balance)
    {
        var response = await _client.PostAsJsonAsync("/api/accounts", new { ownerName = "Test Owner" }, _ct);
        response.EnsureSuccessStatusCode();
        var account = (await response.Content.ReadFromJsonAsync<AccountResponse>(_ct))!;
        if (balance > 0)
        {
            await Deposit(account.Id, balance);
        }

        return account;
    }

    private async Task<TransferResponse> Deposit(Guid accountId, long amount)
    {
        var response = await _client.PostWithKeyAsync($"/api/accounts/{accountId}/deposits", new { amount }, _ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransferResponse>(_ct))!;
    }

    private Task<HttpResponseMessage> PostTransfer(Guid from, Guid to, long amount) =>
        _client.PostWithKeyAsync("/api/transfers", new { fromAccountId = from, toAccountId = to, amount }, _ct);
}

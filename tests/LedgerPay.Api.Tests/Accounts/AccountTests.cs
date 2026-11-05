using System.Net;
using System.Net.Http.Json;
using LedgerPay.Api.Accounts;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace LedgerPay.Api.Tests.Accounts;

public class AccountTests(LedgerPayApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Opening_an_account_returns_201_with_a_zero_balance()
    {
        var response = await _client.PostAsJsonAsync("/api/accounts", new { ownerName = "Ali Yilmaz" }, _ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = await response.Content.ReadFromJsonAsync<AccountResponse>(_ct);
        Assert.NotNull(account);
        Assert.Equal("Ali Yilmaz", account.OwnerName);
        Assert.Equal(0, account.Balance);
        Assert.Equal($"/api/accounts/{account.Id}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task An_opened_account_can_be_read_back()
    {
        var opened = await OpenAccount("Ayse Demir");

        var account = await _client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{opened.Id}", _ct);

        Assert.NotNull(account);
        Assert.Equal(opened.Id, account.Id);
        Assert.Equal("Ayse Demir", account.OwnerName);
        Assert.Equal(0, account.Balance);
    }

    [Fact]
    public async Task Reading_an_unknown_account_returns_404()
    {
        var response = await _client.GetAsync($"/api/accounts/{Guid.CreateVersion7()}", _ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_ct);
        Assert.Equal("Account not found", problem?.Title);
    }

    [Fact]
    public async Task Reading_an_account_with_an_id_that_is_not_a_guid_returns_a_problem()
    {
        var response = await _client.GetAsync("/api/accounts/abc", _ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Opening_an_account_without_an_owner_name_returns_400(string? ownerName)
    {
        var response = await _client.PostAsJsonAsync("/api/accounts", new { ownerName }, _ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(_ct);
        Assert.Contains("OwnerName", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Opening_an_account_with_a_201_character_owner_name_returns_400()
    {
        var response = await _client.PostAsJsonAsync("/api/accounts", new { ownerName = new string('a', 201) }, _ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_external_funding_account_exists_from_the_start()
    {
        var account = await _client.GetFromJsonAsync<AccountResponse>(
            "/api/accounts/00000000-0000-0000-0000-000000000001", _ct);

        Assert.Equal("External funding", account?.OwnerName);
    }

    private async Task<AccountResponse> OpenAccount(string ownerName)
    {
        var response = await _client.PostAsJsonAsync("/api/accounts", new { ownerName }, _ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>(_ct))!;
    }
}

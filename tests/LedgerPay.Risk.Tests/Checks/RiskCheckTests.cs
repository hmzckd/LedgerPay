using System.Globalization;
using Grpc.Net.Client;
using LedgerPay.Risk.V1;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LedgerPay.Risk.Tests.Checks;

public class RiskCheckTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public RiskCheckTests() => CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("tr-TR");

    [Fact]
    public async Task An_amount_up_to_the_limit_is_approved()
    {
        var response = await Check(_factory, 5_000_000);

        Assert.Equal(Decision.Approved, response.Decision);
        Assert.Equal("", response.Reason);
    }

    [Fact]
    public async Task An_amount_over_the_limit_is_rejected_with_a_reason()
    {
        var response = await Check(_factory, 5_000_001);

        Assert.Equal(Decision.Rejected, response.Decision);
        Assert.Equal("The amount is over the 50000.00 TRY limit.", response.Reason);
    }

    [Fact]
    public async Task The_limit_comes_from_configuration()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting("Risk:MaxAmount", "1000"));

        var approved = await Check(factory, 1000);
        var rejected = await Check(factory, 1001);

        Assert.Equal(Decision.Approved, approved.Decision);
        Assert.Equal(Decision.Rejected, rejected.Decision);
        Assert.Equal("The amount is over the 10.00 TRY limit.", rejected.Reason);
    }

    public void Dispose() => _factory.Dispose();

    private async Task<CheckTransferResponse> Check(WebApplicationFactory<Program> factory, long amount)
    {
        using var channel = GrpcChannel.ForAddress(
            factory.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });
        var client = new RiskCheck.RiskCheckClient(channel);

        return await client.CheckTransferAsync(
            new CheckTransferRequest
            {
                FromAccountId = Guid.CreateVersion7().ToString(),
                ToAccountId = Guid.CreateVersion7().ToString(),
                Amount = amount,
            },
            cancellationToken: _ct);
    }
}

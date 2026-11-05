using System.Net;
using Xunit;

namespace LedgerPay.Api.Tests;

public class HealthTests(LedgerPayApiFactory factory)
{
    [Fact]
    public async Task Health_returns_200()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

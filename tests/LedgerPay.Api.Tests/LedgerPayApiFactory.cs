using LedgerPay.Risk.V1;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

[assembly: AssemblyFixture(typeof(LedgerPay.Api.Tests.LedgerPayApiFactory))]

namespace LedgerPay.Api.Tests;

public class LedgerPayApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4.2-management").Build();

    public string RabbitMqConnectionString => _rabbitMq.GetConnectionString();

    public FakeRiskClient Risk { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());
        StartServer();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Ledger", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:RabbitMq", _rabbitMq.GetConnectionString());
        builder.UseSetting("Outbox:RelayEnabled", "false");
        builder.UseSetting("Reconciliation:ScheduleEnabled", "false");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<RiskCheck.RiskCheckClient>();
            services.AddSingleton<RiskCheck.RiskCheckClient>(Risk);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
    }
}

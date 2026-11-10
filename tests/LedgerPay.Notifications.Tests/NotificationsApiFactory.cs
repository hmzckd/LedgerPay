using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

[assembly: AssemblyFixture(typeof(LedgerPay.Notifications.Tests.NotificationsApiFactory))]

namespace LedgerPay.Notifications.Tests;

public class NotificationsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4.2-management").Build();

    public string RabbitMqConnectionString => _rabbitMq.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("tr-TR");
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());
        StartServer();
        await WaitUntilTheQueueExists();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Notifications", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:RabbitMq", _rabbitMq.GetConnectionString());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
    }

    private async Task WaitUntilTheQueueExists()
    {
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            await using var channel = await connection.CreateChannelAsync();
            try
            {
                await channel.QueueDeclarePassiveAsync("ledgerpay.notifications");
                return;
            }
            catch (OperationInterruptedException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }
        }
    }
}

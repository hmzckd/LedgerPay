using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LedgerPay.Api.Accounts;
using LedgerPay.Api.Outbox;
using LedgerPay.Api.Transfers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Xunit;

namespace LedgerPay.Api.Tests.Outbox;

[CollectionDefinition(nameof(OutboxTests), DisableParallelization = true)]
public class OutboxTestsCollection;

[Collection(nameof(OutboxTests))]
public class OutboxTests(LedgerPayApiFactory factory) : IAsyncLifetime
{
    private static readonly Guid ExternalFundingId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly HttpClient _client = factory.CreateClient();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private IConnection _connection = null!;
    private IChannel _channel = null!;
    private string _queue = null!;

    public async ValueTask InitializeAsync()
    {
        var connectionFactory = new ConnectionFactory { Uri = new Uri(factory.RabbitMqConnectionString) };
        _connection = await connectionFactory.CreateConnectionAsync(_ct);
        _channel = await _connection.CreateChannelAsync(cancellationToken: _ct);
        await _channel.ExchangeDeclareAsync("ledgerpay.events", ExchangeType.Topic, durable: true, cancellationToken: _ct);
        _queue = (await _channel.QueueDeclareAsync(cancellationToken: _ct)).QueueName;
        await _channel.QueueBindAsync(_queue, "ledgerpay.events", "transfer.completed", cancellationToken: _ct);
    }

    [Fact]
    public async Task A_transfer_publishes_one_transfer_completed_event()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);
        var transfer = await Transfer(ali.Id, ayse.Id, 4000);

        var message = Assert.Single(await PublishPendingEvents(), m => m.Event.TransferId == transfer.Id);

        Assert.Equal((ali.Id, ayse.Id, 4000L), (message.Event.FromAccountId, message.Event.ToAccountId, message.Event.Amount));
        Assert.Equal(message.Event.EventId.ToString(), message.MessageId);
        Assert.Equal("transfer.completed", message.RoutingKey);
        Assert.True(message.Persistent);
        Assert.DoesNotContain(await PublishPendingEvents(), m => m.Event.TransferId == transfer.Id);
    }

    [Fact]
    public async Task A_deposit_publishes_an_event_from_the_external_funding_account()
    {
        var ali = await OpenAccountWith(0);
        var deposit = await Deposit(ali.Id, 2500);

        var message = Assert.Single(await PublishPendingEvents(), m => m.Event.TransferId == deposit.Id);

        Assert.Equal((ExternalFundingId, ali.Id, 2500L), (message.Event.FromAccountId, message.Event.ToAccountId, message.Event.Amount));
    }

    [Fact]
    public async Task A_rejected_transfer_publishes_nothing()
    {
        var ali = await OpenAccountWith(5000);
        var ayse = await OpenAccountWith(0);

        var response = await PostTransfer(ali.Id, ayse.Id, 8000, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var events = await PublishPendingEvents();
        Assert.Single(events, m => m.Event.ToAccountId == ali.Id);
        Assert.DoesNotContain(events, m => m.Event.FromAccountId == ali.Id);
    }

    [Fact]
    public async Task A_replayed_transfer_publishes_no_second_event()
    {
        var ali = await OpenAccountWith(10000);
        var ayse = await OpenAccountWith(0);
        var key = Guid.NewGuid().ToString();
        await PostTransfer(ali.Id, ayse.Id, 4000, key);
        await PostTransfer(ali.Id, ayse.Id, 4000, key);

        Assert.Single(await PublishPendingEvents(), m => m.Event.FromAccountId == ali.Id);
    }

    [Fact]
    public async Task An_event_that_could_not_be_published_is_sent_in_a_later_round()
    {
        var ali = await OpenAccountWith(0);
        var deposit = await Deposit(ali.Id, 2500);
        var unreachable = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:RabbitMq"] = "amqp://guest:guest@localhost:1/" })
            .Build();
        var relayWithoutBroker = ActivatorUtilities.CreateInstance<OutboxRelay>(
            factory.Services, new RabbitMqPublisher(unreachable));

        Assert.Equal(0, await relayWithoutBroker.SendPendingAsync(_ct));
        Assert.Single(await PublishPendingEvents(), m => m.Event.TransferId == deposit.Id);
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.QueueDeleteAsync(_queue);
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<List<ReceivedMessage>> PublishPendingEvents()
    {
        var relay = factory.Services.GetRequiredService<OutboxRelay>();
        while (await relay.SendPendingAsync(_ct) > 0)
        {
        }

        var messages = new List<ReceivedMessage>();
        while (await _channel.BasicGetAsync(_queue, autoAck: true, _ct) is { } message)
        {
            var published = JsonSerializer.Deserialize<PublishedEvent>(message.Body.Span, JsonSerializerOptions.Web)!;
            messages.Add(new ReceivedMessage(
                published, message.BasicProperties.MessageId, message.RoutingKey, message.BasicProperties.Persistent));
        }

        return messages;
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

    private Task<HttpResponseMessage> PostTransfer(Guid from, Guid to, long amount, string key) =>
        _client.PostWithKeyAsync("/api/transfers", new { fromAccountId = from, toAccountId = to, amount }, _ct, key);

    private async Task<TransferResponse> Transfer(Guid from, Guid to, long amount)
    {
        var response = await PostTransfer(from, to, amount, Guid.NewGuid().ToString());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransferResponse>(_ct))!;
    }

    private record PublishedEvent(
        Guid EventId, Guid TransferId, Guid FromAccountId, Guid ToAccountId, long Amount, DateTimeOffset OccurredAt);

    private record ReceivedMessage(PublishedEvent Event, string? MessageId, string RoutingKey, bool Persistent);
}

using System.Net.Http.Json;
using System.Text;
using LedgerPay.Api.Accounts;
using LedgerPay.Api.Outbox;
using LedgerPay.Api.Tests.Outbox;
using LedgerPay.Api.Transfers;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Xunit;

namespace LedgerPay.Api.Tests.Tracing;

[Collection(nameof(OutboxTests))]
public class TraceTests(LedgerPayApiFactory factory) : IAsyncLifetime
{
    private readonly TraceParent _caller = TraceParent.New();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private HttpClient _client = null!;
    private IConnection _connection = null!;
    private IChannel _channel = null!;
    private string _queue = null!;

    public async ValueTask InitializeAsync()
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("traceparent", _caller.ToString());

        var connectionFactory = new ConnectionFactory { Uri = new Uri(factory.RabbitMqConnectionString) };
        _connection = await connectionFactory.CreateConnectionAsync(_ct);
        _channel = await _connection.CreateChannelAsync(cancellationToken: _ct);
        await _channel.ExchangeDeclareAsync(
            "ledgerpay.events", ExchangeType.Topic, durable: true, cancellationToken: _ct);
        _queue = (await _channel.QueueDeclareAsync(cancellationToken: _ct)).QueueName;
        await _channel.QueueBindAsync(_queue, "ledgerpay.events", "transfer.completed", cancellationToken: _ct);
    }

    [Fact]
    public async Task A_transfer_event_carries_the_trace_of_its_request()
    {
        var transfer = await TransferBetweenNewAccounts();

        await SendPendingEvents();

        var traceParent = await TraceParentOfTheEventFor(transfer.Id);
        Assert.StartsWith($"00-{_caller.TraceId}-", traceParent);
    }

    [Fact]
    public async Task The_relay_sends_the_event_inside_the_trace_of_its_request()
    {
        await TransferBetweenNewAccounts();

        await SendPendingEvents();

        var spans = factory.Spans.InTrace(_caller.TraceId);
        var request = Assert.Single(spans, s => s.DisplayName == "POST /api/transfers/");
        Assert.Equal(_caller.SpanId, request.ParentSpanId);
        Assert.Contains(spans, s => s.Source.Name == "Npgsql" && s.ParentSpanId == request.SpanId);
        var send = Assert.Single(
            spans, s => s.DisplayName == "send outbox message" && s.ParentSpanId == request.SpanId);
        Assert.Single(spans, s => s.DisplayName == "publish transfer.completed" && s.ParentSpanId == send.SpanId);
    }

    [Fact]
    public async Task Database_queries_outside_a_request_leave_no_spans()
    {
        await SendPendingEvents();

        Assert.DoesNotContain(factory.Spans.All, s => s.Source.Name == "Npgsql" && s.ParentSpanId == default);
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.QueueDeleteAsync(_queue);
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task SendPendingEvents()
    {
        var relay = factory.Services.GetRequiredService<OutboxRelay>();
        while (await relay.SendPendingAsync(_ct) > 0)
        {
        }
    }

    private async Task<string?> TraceParentOfTheEventFor(Guid transferId)
    {
        while (await _channel.BasicGetAsync(_queue, autoAck: true, _ct) is { } message)
        {
            if (Encoding.UTF8.GetString(message.Body.Span).Contains(transferId.ToString()))
            {
                var headers = message.BasicProperties.Headers ?? new Dictionary<string, object?>();
                return headers.TryGetValue("traceparent", out var value)
                    ? Encoding.UTF8.GetString((byte[])value!)
                    : null;
            }
        }

        throw new InvalidOperationException($"No event was published for transfer {transferId}.");
    }

    private async Task<TransferResponse> TransferBetweenNewAccounts()
    {
        var ali = await OpenAccount();
        var ayse = await OpenAccount();
        (await _client.PostWithKeyAsync($"/api/accounts/{ali.Id}/deposits", new { amount = 10000 }, _ct))
            .EnsureSuccessStatusCode();

        var response = await _client.PostWithKeyAsync(
            "/api/transfers", new { fromAccountId = ali.Id, toAccountId = ayse.Id, amount = 4000 }, _ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransferResponse>(_ct))!;
    }

    private async Task<AccountResponse> OpenAccount()
    {
        var response = await _client.PostAsJsonAsync("/api/accounts", new { ownerName = "Test Owner" }, _ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>(_ct))!;
    }
}

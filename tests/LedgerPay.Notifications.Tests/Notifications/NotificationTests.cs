using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using Xunit;

namespace LedgerPay.Notifications.Tests.Notifications;

public class NotificationTests(NotificationsApiFactory factory) : IAsyncLifetime
{
    private static readonly Guid ExternalFundingId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly HttpClient _client = factory.CreateClient();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private IConnection _connection = null!;
    private IChannel _channel = null!;

    public async ValueTask InitializeAsync()
    {
        var connectionFactory = new ConnectionFactory { Uri = new Uri(factory.RabbitMqConnectionString) };
        _connection = await connectionFactory.CreateConnectionAsync(_ct);
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), _ct);
    }

    [Fact]
    public async Task A_transfer_completed_event_becomes_a_notification_for_the_receiver()
    {
        var completed = NewEvent(Guid.CreateVersion7(), 4000);

        await Publish(completed);

        var notification = Assert.Single(await WaitForNotifications(completed.ToAccountId, 1));
        Assert.Equal(completed.TransferId, notification.TransferId);
        Assert.Equal(completed.FromAccountId, notification.FromAccountId);
        Assert.Equal(4000, notification.Amount);
        Assert.Equal("You received 40.00 TRY.", notification.Message);
    }

    [Fact]
    public async Task The_sender_gets_no_notification()
    {
        var completed = NewEvent(Guid.CreateVersion7(), 4000);
        var marker = NewEvent(Guid.CreateVersion7(), 100) with { ToAccountId = completed.FromAccountId };

        await Publish(completed);
        await Publish(marker);

        var notification = Assert.Single(await WaitForNotifications(completed.FromAccountId, 1));
        Assert.Equal(marker.TransferId, notification.TransferId);
    }

    [Fact]
    public async Task The_same_event_twice_gives_one_notification_and_is_not_dead_lettered()
    {
        var completed = NewEvent(Guid.CreateVersion7(), 4000);
        var later = NewEvent(Guid.CreateVersion7(), 100) with { ToAccountId = completed.ToAccountId };

        await Publish(completed);
        await WaitForNotifications(completed.ToAccountId, 1);
        await Publish(completed);
        await Publish(later);

        var notifications = await WaitForNotifications(completed.ToAccountId, 2);
        Assert.Equal(2, notifications.Count);
        Assert.Single(notifications, n => n.TransferId == completed.TransferId);
        Assert.Null(await FindDeadLetter(completed.EventId.ToString(), TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task A_deposit_notification_comes_from_the_external_funding_account()
    {
        var completed = NewEvent(ExternalFundingId, 2550);

        await Publish(completed);

        var notification = Assert.Single(await WaitForNotifications(completed.ToAccountId, 1));
        Assert.Equal(ExternalFundingId, notification.FromAccountId);
        Assert.Equal("You received 25.50 TRY.", notification.Message);
    }

    [Fact]
    public async Task An_unreadable_message_goes_to_the_dead_letter_queue_after_the_delivery_limit()
    {
        var messageId = Guid.CreateVersion7().ToString();

        await PublishRaw("this is not json", messageId);

        var deadLetter = await FindDeadLetter(messageId, TimeSpan.FromSeconds(10));
        Assert.NotNull(deadLetter);
        Assert.Equal("this is not json", Encoding.UTF8.GetString(deadLetter.Body.Span));
        Assert.Equal("delivery_limit", Header(deadLetter, "x-first-death-reason"));
    }

    [Fact]
    public async Task An_unknown_account_has_no_notifications()
    {
        var notifications = await _client.GetFromJsonAsync<List<NotificationResponse>>(
            $"/api/accounts/{Guid.CreateVersion7()}/notifications", _ct);

        Assert.Empty(notifications!);
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static TransferCompletedEvent NewEvent(Guid fromAccountId, long amount)
    {
        var transferId = Guid.CreateVersion7();
        return new TransferCompletedEvent(
            Guid.CreateVersion7(), transferId, fromAccountId, Guid.CreateVersion7(), amount, DateTimeOffset.UtcNow);
    }

    private Task Publish(TransferCompletedEvent completed) =>
        PublishRaw(JsonSerializer.Serialize(completed, JsonSerializerOptions.Web), completed.EventId.ToString());

    private async Task PublishRaw(string body, string messageId)
    {
        var properties = new BasicProperties { MessageId = messageId, DeliveryMode = DeliveryModes.Persistent };
        await _channel.BasicPublishAsync(
            "ledgerpay.events", "transfer.completed", mandatory: false, properties, Encoding.UTF8.GetBytes(body), _ct);
    }

    private async Task<List<NotificationResponse>> WaitForNotifications(Guid accountId, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var notifications = await _client.GetFromJsonAsync<List<NotificationResponse>>(
                $"/api/accounts/{accountId}/notifications", _ct);
            if (notifications!.Count >= count || DateTime.UtcNow > deadline)
            {
                return notifications;
            }

            await Task.Delay(100, _ct);
        }
    }

    private async Task<BasicGetResult?> FindDeadLetter(string messageId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await _channel.BasicGetAsync("ledgerpay.notifications.dead-letter", autoAck: true, _ct) is { } message
                && message.BasicProperties.MessageId == messageId)
            {
                return message;
            }

            await Task.Delay(100, _ct);
        }

        return null;
    }

    private static string Header(BasicGetResult message, string name) =>
        Encoding.UTF8.GetString((byte[])message.BasicProperties.Headers![name]!);

    private record TransferCompletedEvent(
        Guid EventId, Guid TransferId, Guid FromAccountId, Guid ToAccountId, long Amount, DateTimeOffset OccurredAt);

    private record NotificationResponse(
        Guid Id, Guid TransferId, Guid FromAccountId, long Amount, string Message, DateTimeOffset OccurredAt);
}

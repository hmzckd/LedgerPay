using System.Text.Json;
using LedgerPay.Notifications.Data;
using LedgerPay.Notifications.Notifications;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace LedgerPay.Notifications.Messaging;

public class TransferCompletedConsumer(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<TransferCompletedConsumer> logger) : BackgroundService
{
    public const string Exchange = "ledgerpay.events";
    public const string RoutingKey = "transfer.completed";
    public const string Queue = "ledgerpay.notifications";
    public const string DeadLetterQueue = "ledgerpay.notifications.dead-letter";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { RespectRequiredConstructorParameters = true };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Could not listen to RabbitMQ: {Error}. Trying again in 5 seconds.", e.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken);
            }
        }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(configuration.GetConnectionString("RabbitMq")!),
            AutomaticRecoveryEnabled = false,
        };
        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        var closed = new TaskCompletionSource();
        connection.ConnectionShutdownAsync += (_, _) =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(
            DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
            cancellationToken: ct);
        await channel.QueueDeclareAsync(
            Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-delivery-limit"] = 2,
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = DeadLetterQueue,
            },
            cancellationToken: ct);
        await channel.QueueBindAsync(Queue, Exchange, RoutingKey, cancellationToken: ct);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, delivery, ct);
        await channel.BasicConsumeAsync(Queue, autoAck: false, consumer, ct);
        logger.LogInformation("Listening to {Queue}.", Queue);

        await closed.Task.WaitAsync(ct);
        logger.LogWarning("The connection to RabbitMQ was closed.");
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        try
        {
            var completed = JsonSerializer.Deserialize<TransferCompleted>(delivery.Body.Span, JsonOptions)
                ?? throw new JsonException("The message is empty.");
            await SaveAsync(completed, ct);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Could not handle message {MessageId}: {Error}", delivery.BasicProperties.MessageId, e.Message);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, ct);
        }
    }

    private async Task SaveAsync(TransferCompleted completed, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.Notifications.Add(Notification.For(completed, clock));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            logger.LogInformation("Event {EventId} was already handled.", completed.EventId);
        }
    }
}

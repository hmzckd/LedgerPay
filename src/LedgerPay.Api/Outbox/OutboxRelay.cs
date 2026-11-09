using LedgerPay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LedgerPay.Api.Outbox;

public class OutboxRelay(
    IServiceScopeFactory scopes,
    RabbitMqPublisher publisher,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<OutboxRelay> logger) : BackgroundService
{
    private const int BatchSize = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Outbox:RelayEnabled", true))
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SendPendingAsync(stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(e, "The outbox relay round failed.");
            }
        }
    }

    public async Task<int> SendPendingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var messages = await db.OutboxMessages
            .FromSql($"""
                SELECT * FROM outbox_messages
                WHERE sent_at IS NULL
                ORDER BY created_at, id
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var message in messages)
        {
            try
            {
                await publisher.PublishAsync(message, ct);
                message.MarkSent(clock);
                sent++;
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                message.MarkFailed(e.Message);
                logger.LogWarning("Could not publish outbox message {MessageId}: {Error}", message.Id, e.Message);
                break;
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return sent;
    }
}

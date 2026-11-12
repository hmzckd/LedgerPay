namespace LedgerPay.Api.Reconciliation;

public class NightlyReconciliation(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<NightlyReconciliation> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Reconciliation:ScheduleEnabled", true))
        {
            return;
        }

        var runAt = configuration.GetValue("Reconciliation:RunAt", new TimeOnly(0, 5));
        logger.LogInformation("Reconciliation runs every night at {RunAt:HH:mm} UTC.", runAt);

        while (!stoppingToken.IsCancellationRequested)
        {
            var day = DueDay(clock.GetUtcNow(), runAt);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<Reconciler>().RunScheduledAsync(day, stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(e, "The reconciliation of {Day:O} could not run. Trying again in 1 minute.", day);
                await Task.Delay(RetryDelay, clock, stoppingToken);
                continue;
            }

            var wait = NextRunAt(day, runAt) - clock.GetUtcNow();
            await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, clock, stoppingToken);
        }
    }

    private static DateOnly DueDay(DateTimeOffset now, TimeOnly runAt) =>
        DateOnly.FromDateTime((now - runAt.ToTimeSpan()).UtcDateTime).AddDays(-1);

    private static DateTimeOffset NextRunAt(DateOnly day, TimeOnly runAt) =>
        new(day.AddDays(2).ToDateTime(runAt), TimeSpan.Zero);
}

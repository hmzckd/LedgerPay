using System.Net.Http.Json;
using LedgerPay.Api.Data;
using LedgerPay.Api.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LedgerPay.Api.Tests.Reconciliation;

[Collection(nameof(ReconciliationTests))]
public class NightlyReconciliationTests(LedgerPayApiFactory factory) : IAsyncDisposable
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly List<NightlyReconciliation> _jobs = [];

    [Fact]
    public async Task A_missed_night_is_reconciled_when_the_app_starts()
    {
        var clock = new FakeTimeProvider(At(2025, 3, 10, 8, 0));

        await StartJob(clock);

        var run = Assert.Single(await WaitForScheduledRuns(new DateOnly(2025, 3, 9)));
        Assert.Equal(ReconciliationStatus.Passed, run.Status);
    }

    [Fact]
    public async Task The_next_night_is_reconciled_at_five_past_midnight()
    {
        var clock = new FakeTimeProvider(At(2025, 4, 10, 12, 0));
        await StartJob(clock);
        await WaitForScheduledRuns(new DateOnly(2025, 4, 9));

        clock.SetUtcNow(At(2025, 4, 11, 0, 4));
        await Task.Delay(500, _ct);
        Assert.Empty(await ScheduledRunsFor(new DateOnly(2025, 4, 10)));

        clock.SetUtcNow(At(2025, 4, 11, 0, 5));
        Assert.Single(await WaitForScheduledRuns(new DateOnly(2025, 4, 10)));
    }

    [Fact]
    public async Task Two_copies_of_the_app_reconcile_each_night_once()
    {
        var clock = new FakeTimeProvider(At(2025, 5, 10, 8, 0));
        await StartJob(clock);
        await StartJob(clock);

        await WaitForScheduledRuns(new DateOnly(2025, 5, 9));
        await Task.Delay(1000, _ct);
        Assert.Single(await ScheduledRunsFor(new DateOnly(2025, 5, 9)));

        clock.SetUtcNow(At(2025, 5, 11, 0, 5));
        await WaitForScheduledRuns(new DateOnly(2025, 5, 10));
        await Task.Delay(1000, _ct);
        Assert.Single(await ScheduledRunsFor(new DateOnly(2025, 5, 10)));
    }

    [Fact]
    public async Task Manual_runs_are_not_limited_to_one_a_day()
    {
        var day = new DateOnly(2025, 6, 9);
        await StartJob(new FakeTimeProvider(At(2025, 6, 10, 8, 0)));
        await WaitForScheduledRuns(day);

        (await _client.PostAsJsonAsync("/api/reconciliation-runs", new { day }, _ct)).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync("/api/reconciliation-runs", new { day }, _ct)).EnsureSuccessStatusCode();

        var runs = await RunsFor(day);
        Assert.Single(runs, r => r.Kind == ReconciliationKind.Scheduled);
        Assert.Equal(2, runs.Count(r => r.Kind == ReconciliationKind.Manual));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var job in _jobs)
        {
            await job.StopAsync(CancellationToken.None);
            job.Dispose();
        }
    }

    private static DateTimeOffset At(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    private async Task StartJob(FakeTimeProvider clock)
    {
        var job = new NightlyReconciliation(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            clock,
            NullLogger<NightlyReconciliation>.Instance);
        _jobs.Add(job);
        await job.StartAsync(_ct);
    }

    private async Task<List<ReconciliationRun>> WaitForScheduledRuns(DateOnly day)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var runs = await ScheduledRunsFor(day);
            if (runs.Count > 0 || DateTime.UtcNow > deadline)
            {
                return runs;
            }

            await Task.Delay(50, _ct);
        }
    }

    private async Task<List<ReconciliationRun>> ScheduledRunsFor(DateOnly day) =>
        (await RunsFor(day)).Where(r => r.Kind == ReconciliationKind.Scheduled).ToList();

    private async Task<List<ReconciliationRun>> RunsFor(DateOnly day)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        return await db.ReconciliationRuns.Where(r => r.Day == day).ToListAsync(_ct);
    }
}

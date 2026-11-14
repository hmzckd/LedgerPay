using System.Collections.Concurrent;
using System.Diagnostics;
using OpenTelemetry;

namespace LedgerPay.Risk.Tests;

public class SpanCollector : BaseProcessor<Activity>
{
    private readonly ConcurrentQueue<Activity> _spans = new();

    public IReadOnlyList<Activity> All => _spans.ToList();

    public IReadOnlyList<Activity> InTrace(ActivityTraceId traceId) => _spans.Where(s => s.TraceId == traceId).ToList();

    public async Task<IReadOnlyList<Activity>> WaitForTrace(
        ActivityTraceId traceId, string lastSpan, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var spans = InTrace(traceId);
            if (spans.Any(s => s.DisplayName == lastSpan) || DateTime.UtcNow > deadline)
            {
                return spans;
            }

            await Task.Delay(100, ct);
        }
    }

    public override void OnEnd(Activity span) => _spans.Enqueue(span);
}

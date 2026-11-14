using System.Diagnostics;

namespace LedgerPay.Api.Tests;

public record TraceParent(ActivityTraceId TraceId, ActivitySpanId SpanId)
{
    public static TraceParent New() => new(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom());

    public override string ToString() => $"00-{TraceId}-{SpanId}-01";
}

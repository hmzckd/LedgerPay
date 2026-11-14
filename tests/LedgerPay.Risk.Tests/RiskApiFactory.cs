using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using OpenTelemetry.Trace;

namespace LedgerPay.Risk.Tests;

public class RiskApiFactory : WebApplicationFactory<Program>
{
    public SpanCollector Spans { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Tracing:ExportEnabled", "false");
        builder.ConfigureTestServices(services =>
            services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(Spans)));
    }
}

using LedgerPay.Risk.Checks;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddOptions<RiskRules>()
    .BindConfiguration(RiskRules.Section)
    .Validate(rules => rules.MaxAmount > 0, "Risk:MaxAmount must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("ledgerpay-risk"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        if (builder.Configuration.GetValue("Tracing:ExportEnabled", true))
        {
            tracing.AddOtlpExporter();
        }
    });

var app = builder.Build();

app.MapGrpcService<RiskCheckService>();

app.Run();

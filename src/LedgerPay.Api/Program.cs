using LedgerPay.Api.Accounts;
using LedgerPay.Api.Data;
using LedgerPay.Api.Outbox;
using LedgerPay.Api.Reconciliation;
using LedgerPay.Api.Risk;
using LedgerPay.Api.Transfers;
using LedgerPay.Risk.V1;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<LedgerDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Ledger"),
    npgsql => npgsql.ConfigureDataSource(dataSource => dataSource.ConfigureTracing(QueryTracing.OnlyInsideATrace))));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddHealthChecks().AddDbContextCheck<LedgerDbContext>();
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddSingleton<OutboxRelay>();
builder.Services.AddHostedService(services => services.GetRequiredService<OutboxRelay>());

var riskAddress = builder.Configuration["Risk:Address"]
    ?? throw new InvalidOperationException("Risk:Address is not set.");
builder.Services.AddGrpcClient<RiskCheck.RiskCheckClient>(options => options.Address = new Uri(riskAddress));
builder.Services.AddScoped<RiskChecker>();
builder.Services.AddScoped<Reconciler>();
builder.Services.AddHostedService<NightlyReconciliation>();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("ledgerpay-api"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddNpgsql()
            .AddSource("Grpc.Net.Client", OutboxRelay.TracingSource, RabbitMQActivitySource.PublisherSourceName);
        if (builder.Configuration.GetValue("Tracing:ExportEnabled", true))
        {
            tracing.AddOtlpExporter();
        }
    });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHealthChecks("/health");
app.MapAccountEndpoints();
app.MapTransferEndpoints();
app.MapReconciliationEndpoints();

app.Run();

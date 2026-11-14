using LedgerPay.Notifications.Data;
using LedgerPay.Notifications.Messaging;
using LedgerPay.Notifications.Notifications;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<NotificationsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Notifications")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<NotificationsDbContext>();
builder.Services.AddHostedService<TransferCompletedConsumer>();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("ledgerpay-notifications"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddNpgsql()
            .AddSource(RabbitMQActivitySource.SubscriberSourceName);
        if (builder.Configuration.GetValue("Tracing:ExportEnabled", true))
        {
            tracing.AddOtlpExporter();
        }
    });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHealthChecks("/health");
app.MapNotificationEndpoints();

app.Run();

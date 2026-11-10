using LedgerPay.Notifications.Data;
using LedgerPay.Notifications.Messaging;
using LedgerPay.Notifications.Notifications;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<NotificationsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Notifications")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<NotificationsDbContext>();
builder.Services.AddHostedService<TransferCompletedConsumer>();

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

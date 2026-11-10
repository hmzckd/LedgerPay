using LedgerPay.Notifications.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LedgerPay.Notifications.Notifications;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/accounts/{accountId:guid}/notifications", List);
    }

    private static async Task<Ok<List<NotificationResponse>>> List(
        Guid accountId, NotificationsDbContext db, CancellationToken ct)
    {
        var notifications = await db.Notifications
            .Where(n => n.AccountId == accountId)
            .OrderByDescending(n => n.OccurredAt)
            .ThenByDescending(n => n.Id)
            .Take(50)
            .Select(n => new NotificationResponse(n.Id, n.TransferId, n.FromAccountId, n.Amount, n.Message, n.OccurredAt))
            .ToListAsync(ct);

        return TypedResults.Ok(notifications);
    }
}

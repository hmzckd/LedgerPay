using LedgerPay.Notifications.Notifications;
using Microsoft.EntityFrameworkCore;

namespace LedgerPay.Notifications.Data;

public class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(notification =>
        {
            notification.ToTable("notifications");
            notification.HasKey(n => n.Id);
            notification.Property(n => n.Id).HasColumnName("id");
            notification.Property(n => n.EventId).HasColumnName("event_id");
            notification.Property(n => n.AccountId).HasColumnName("account_id");
            notification.Property(n => n.TransferId).HasColumnName("transfer_id");
            notification.Property(n => n.FromAccountId).HasColumnName("from_account_id");
            notification.Property(n => n.Amount).HasColumnName("amount");
            notification.Property(n => n.Message).HasColumnName("message").HasMaxLength(200);
            notification.Property(n => n.OccurredAt).HasColumnName("occurred_at");
            notification.Property(n => n.CreatedAt).HasColumnName("created_at");
            notification.HasIndex(n => n.EventId).IsUnique();
            notification.HasIndex(n => new { n.AccountId, n.OccurredAt });
        });
    }
}

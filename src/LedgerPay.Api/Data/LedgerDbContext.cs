using LedgerPay.Api.Accounts;
using Microsoft.EntityFrameworkCore;

namespace LedgerPay.Api.Data;

public class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(account =>
        {
            account.ToTable("accounts");
            account.HasKey(a => a.Id);
            account.Property(a => a.Id).HasColumnName("id");
            account.Property(a => a.OwnerName).HasColumnName("owner_name").HasMaxLength(200);
            account.Property(a => a.CreatedAt).HasColumnName("created_at");
            account.HasData(new Account(
                Account.ExternalFundingId,
                "External funding",
                new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)));
        });
    }
}

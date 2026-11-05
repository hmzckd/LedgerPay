using LedgerPay.Api.Accounts;
using LedgerPay.Api.Ledger;
using LedgerPay.Api.Transfers;
using Microsoft.EntityFrameworkCore;

namespace LedgerPay.Api.Data;

public class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

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

        modelBuilder.Entity<Transfer>(transfer =>
        {
            transfer.ToTable("transfers", table =>
            {
                table.HasCheckConstraint("ck_transfers_amount_positive", "amount > 0");
                table.HasCheckConstraint("ck_transfers_different_accounts", "from_account_id <> to_account_id");
            });
            transfer.HasKey(t => t.Id);
            transfer.Property(t => t.Id).HasColumnName("id");
            transfer.Property(t => t.FromAccountId).HasColumnName("from_account_id");
            transfer.Property(t => t.ToAccountId).HasColumnName("to_account_id");
            transfer.Property(t => t.Amount).HasColumnName("amount");
            transfer.Property(t => t.CreatedAt).HasColumnName("created_at");
            transfer.HasOne<Account>().WithMany().HasForeignKey(t => t.FromAccountId).OnDelete(DeleteBehavior.Restrict);
            transfer.HasOne<Account>().WithMany().HasForeignKey(t => t.ToAccountId).OnDelete(DeleteBehavior.Restrict);
            transfer.HasMany(t => t.Entries).WithOne().HasForeignKey(e => e.TransferId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LedgerEntry>(entry =>
        {
            entry.ToTable("ledger_entries");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.TransferId).HasColumnName("transfer_id");
            entry.Property(e => e.AccountId).HasColumnName("account_id");
            entry.Property(e => e.Amount).HasColumnName("amount");
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.HasOne<Account>().WithMany().HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Restrict);
            entry.HasIndex(e => new { e.AccountId, e.CreatedAt });
        });
    }
}

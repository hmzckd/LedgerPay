using LedgerPay.Api.Accounts;
using LedgerPay.Api.Ledger;
using LedgerPay.Api.Outbox;
using LedgerPay.Api.Reconciliation;
using LedgerPay.Api.Transfers;
using Microsoft.EntityFrameworkCore;

namespace LedgerPay.Api.Data;

public class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ReconciliationRun> ReconciliationRuns => Set<ReconciliationRun>();

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
            transfer.Property(t => t.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(Idempotency.MaxKeyLength);
            transfer.HasIndex(t => t.IdempotencyKey).IsUnique();
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

        modelBuilder.Entity<OutboxMessage>(message =>
        {
            message.ToTable("outbox_messages");
            message.HasKey(m => m.Id);
            message.Property(m => m.Id).HasColumnName("id");
            message.Property(m => m.Type).HasColumnName("type").HasMaxLength(100);
            message.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb");
            message.Property(m => m.CreatedAt).HasColumnName("created_at");
            message.Property(m => m.SentAt).HasColumnName("sent_at");
            message.Property(m => m.Attempts).HasColumnName("attempts");
            message.Property(m => m.LastError).HasColumnName("last_error");
            message.Property(m => m.TraceParent).HasColumnName("trace_parent");
            message.HasIndex(m => new { m.CreatedAt, m.Id }).HasFilter("sent_at IS NULL");
        });

        modelBuilder.Entity<ReconciliationRun>(run =>
        {
            run.ToTable("reconciliation_runs");
            run.HasKey(r => r.Id);
            run.Property(r => r.Id).HasColumnName("id");
            run.Property(r => r.Day).HasColumnName("day");
            run.Property(r => r.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
            run.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
            run.Property(r => r.Problems).HasColumnName("problems");
            run.Property(r => r.StartedAt).HasColumnName("started_at");
            run.Property(r => r.FinishedAt).HasColumnName("finished_at");
            run.HasIndex(r => r.Day).IsUnique().HasFilter("kind = 'Scheduled'");
        });
    }
}

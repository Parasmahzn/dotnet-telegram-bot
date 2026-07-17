using MeroShareBot.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace MeroShareBot.Shared.Data;

public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<LinkedAccountEntity> LinkedAccounts => Set<LinkedAccountEntity>();
    public DbSet<ChatSettingsEntity> ChatSettings => Set<ChatSettingsEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<WatchlistEntity> WatchlistItems => Set<WatchlistEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var shareIdSetComparer = new ValueComparer<HashSet<int>>(
            (a, b) => (a ?? new()).SetEquals(b ?? new()),
            s => s.Aggregate(0, (hash, id) => HashCode.Combine(hash, id)),
            s => new HashSet<int>(s));

        modelBuilder.Entity<LinkedAccountEntity>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.ChatId);
            e.HasIndex(a => new { a.Username, a.Dp }).IsUnique();
            e.Property(a => a.Label).HasDefaultValue("");

            e.Property(a => a.AutoApplyPromptedShareIds)
                .HasConversion(
                    s => JsonSerializer.Serialize(s, (JsonSerializerOptions?)null),
                    s => JsonSerializer.Deserialize<HashSet<int>>(s, (JsonSerializerOptions?)null) ?? new())
                .Metadata.SetValueComparer(shareIdSetComparer);

            e.Property(a => a.AppliedShareIds)
                .HasConversion(
                    s => JsonSerializer.Serialize(s, (JsonSerializerOptions?)null),
                    s => JsonSerializer.Deserialize<HashSet<int>>(s, (JsonSerializerOptions?)null) ?? new())
                .Metadata.SetValueComparer(shareIdSetComparer);
        });

        // ChatId is always an explicitly-assigned Telegram chat ID, never a surrogate —
        // without ValueGeneratedNever, EF's convention treats a bare long/int PK as auto-increment.
        modelBuilder.Entity<ChatSettingsEntity>(e =>
        {
            e.HasKey(c => c.ChatId);
            e.Property(c => c.ChatId).ValueGeneratedNever();
        });

        modelBuilder.Entity<UserEntity>(e =>
        {
            e.HasKey(u => u.ChatId);
            e.Property(u => u.ChatId).ValueGeneratedNever();
            e.Property(u => u.IsApplyAllowed).HasDefaultValue(false);
            e.Property(u => u.IsBlocked).HasDefaultValue(false);
        });

        modelBuilder.Entity<WatchlistEntity>().HasKey(w => new { w.ChatId, w.Symbol });
    }
}

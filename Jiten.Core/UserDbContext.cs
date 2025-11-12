using Jiten.Core.Data.Authentication;
using Jiten.Core.Data.FSRS;
using Jiten.Core.Data.User;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Jiten.Core;

public class UserDbContext : IdentityDbContext<User>
{
    public UserDbContext()
    {
    }

    public UserDbContext(DbContextOptions<UserDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserCoverage> UserCoverages { get; set; }
    public DbSet<UserKnownWord> UserKnownWords { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<UserMetadata> UserMetadatas { get; set; }
    public DbSet<ApiKey> ApiKeys { get; set; }
    public DbSet<UserDeckPreference> UserDeckPreferences { get; set; }

    public DbSet<FsrsCard> FsrsCards { get; set; }
    public DbSet<FsrsReviewLog> FsrsReviewLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var guidToString = new ValueConverter<string, Guid>(
            v => Guid.Parse(v),
            v => v.ToString());

        modelBuilder.HasDefaultSchema("user");

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.Id)
                  .HasConversion(guidToString)
                  .HasColumnType("uuid")
                  .IsRequired();
        });

        modelBuilder.Entity<IdentityUserClaim<string>>(entity =>
        {
            entity.Property(e => e.UserId)
                  .HasConversion(guidToString)
                  .HasColumnType("uuid")
                  .IsRequired();
        });

        modelBuilder.Entity<IdentityUserLogin<string>>(entity =>
        {
            entity.Property(e => e.UserId)
                  .HasConversion(guidToString)
                  .HasColumnType("uuid")
                  .IsRequired();
        });

        modelBuilder.Entity<IdentityUserToken<string>>(entity =>
        {
            entity.Property(e => e.UserId)
                  .HasConversion(guidToString)
                  .HasColumnType("uuid")
                  .IsRequired();
        });

        modelBuilder.Entity<IdentityUserRole<string>>(entity =>
        {
            entity.Property(e => e.UserId)
                  .HasConversion(guidToString)
                  .HasColumnType("uuid")
                  .IsRequired();
        });


        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(rt => rt.Token);
            entity.Property(rt => rt.JwtId).IsRequired();
            entity.Property(rt => rt.ExpiryDate).IsRequired();
            entity.Property(rt => rt.UserId).HasConversion(guidToString).HasColumnType("uuid").IsRequired();
            entity.HasOne(rt => rt.User)
                  .WithMany()
                  .HasForeignKey(rt => rt.UserId);

            entity.HasIndex(rt => rt.UserId);
        });

        modelBuilder.Entity<UserCoverage>(entity =>
        {
            entity.HasKey(uc => new { uc.UserId, uc.DeckId }).HasName("PK_UserCoverages");
            entity.Property(uc => uc.Coverage).IsRequired();
            entity.Property(uc => uc.UniqueCoverage).IsRequired();
            entity.Property(uc => uc.UserId).HasConversion(guidToString).HasColumnType("uuid").IsRequired();

            entity.HasIndex(uc => uc.UserId).HasDatabaseName("IX_UserCoverage_UserId");
        });

        modelBuilder.Entity<UserKnownWord>(entity =>
        {
            entity.HasKey(uk => new { uk.UserId, uk.WordId, uk.ReadingIndex });
            entity.Property(uk => uk.LearnedDate).IsRequired();
            entity.Property(uk => uk.KnownState).IsRequired();
            entity.Property(uk => uk.UserId).HasConversion(guidToString).HasColumnType("uuid").IsRequired();

            entity.HasIndex(uk => uk.UserId).HasDatabaseName("IX_UserKnownWord_UserId");
        });

        modelBuilder.Entity<UserMetadata>(entity =>
        {
            entity.HasKey(um => um.UserId);
            entity.Property(um => um.CoverageRefreshedAt).IsRequired(false);
            entity.Property(um => um.UserId).HasConversion(guidToString).HasColumnType("uuid").IsRequired();

            entity.HasOne<User>()
                  .WithOne()
                  .HasForeignKey<UserMetadata>(um => um.UserId);
        });

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(k => k.Id);
            entity.Property(k => k.UserId).HasConversion(guidToString).HasColumnType("uuid").IsRequired();
            entity.Property(k => k.Hash).IsRequired().HasMaxLength(88);
            entity.Property(k => k.CreatedAt).IsRequired();
            entity.Property(k => k.IsRevoked).HasDefaultValue(false);

            entity.HasOne(k => k.User)
                  .WithOne()
                  .HasForeignKey<ApiKey>(k => k.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(k => k.Hash)
                  .IsUnique()
                  .HasDatabaseName("IX_ApiKey_Hash");

            entity.HasIndex(k => k.UserId)
                  .HasDatabaseName("IX_ApiKey_UserId");

            entity.HasIndex(k => new { k.UserId, k.IsRevoked })
                  .HasDatabaseName("IX_ApiKey_UserId_IsRevoked");
        });

        modelBuilder.Entity<UserDeckPreference>(entity =>
        {
            entity.HasKey(udp => new { udp.UserId, udp.DeckId });
            entity.Property(udp => udp.UserId).HasConversion(guidToString).HasColumnType("uuid").IsRequired();
            entity.Property(udp => udp.Status).IsRequired();
            entity.Property(udp => udp.IsFavourite).IsRequired();
            entity.Property(udp => udp.IsIgnored).IsRequired();

            entity.HasOne<User>()
                  .WithMany()
                  .HasForeignKey(udp => udp.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(udp => udp.UserId).HasDatabaseName("IX_UserDeckPreference_UserId");
            entity.HasIndex(udp => new { udp.UserId, udp.IsFavourite }).HasDatabaseName("IX_UserDeckPreference_UserId_IsFavourite");
            entity.HasIndex(udp => new { udp.UserId, udp.Status }).HasDatabaseName("IX_UserDeckPreference_UserId_Status");
            entity.HasIndex(udp => new { udp.UserId, udp.IsIgnored }).HasDatabaseName("IX_UserDeckPreference_UserId_IsIgnored");
        });

        // FSRS
        modelBuilder.Entity<FsrsCard>(entity =>
        {
            entity.HasKey(c => c.CardId);
            entity.Property(c => c.UserId).HasConversion(guidToString).HasColumnType("uuid").IsRequired();
            entity.HasIndex(c => new { c.UserId, c.WordId, c.ReadingIndex }).IsUnique();
            entity.HasIndex(c => c.UserId);
        });

        modelBuilder.Entity<FsrsReviewLog>(entity =>
        {
            entity.HasKey(l => l.ReviewLogId);
            entity.HasOne<FsrsCard>()
                  .WithMany()
                  .HasForeignKey(r => r.CardId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(r => new { r.CardId, r.ReviewDateTime }).IsUnique();
        });


        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges()
    {
        AddTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AddTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void AddTimestamps()
    {
        var userEntities = ChangeTracker.Entries()
                                        .Where(x => x is { Entity: User, State: EntityState.Added or EntityState.Modified });

        foreach (var entity in userEntities)
        {
            var now = DateTime.UtcNow;
            if (entity.State == EntityState.Added)
            {
                ((User)entity.Entity).CreatedAt = now;
            }

            ((User)entity.Entity).UpdatedAt = now;
        }
    }
}
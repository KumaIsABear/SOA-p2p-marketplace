using Auth.API.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Auth.API.Infrastructure.Persistence;

public sealed class AuthDbContext : DbContext {
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ModerationOperation> ModerationOperations => Set<ModerationOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        modelBuilder.Entity<User>(entity => {
            entity.ToTable("Users");
            entity.HasKey(u => u.UserId);

            entity.Property(u => u.Email).IsRequired().HasMaxLength(256);
            entity.Property(u => u.NormalizedEmail).IsRequired().HasMaxLength(256);
            entity.Property(u => u.DisplayName).IsRequired().HasMaxLength(128);
            entity.Property(u => u.PasswordHash).HasMaxLength(256);
            entity.Property(u => u.Role).IsRequired().HasConversion<string>().HasMaxLength(32);
            entity.Property(u => u.Status).IsRequired().HasConversion<string>().HasMaxLength(32);
            entity.Property(u => u.Version).IsRequired().IsConcurrencyToken();

            entity.HasIndex(u => u.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<ExternalIdentity>(entity => {
            entity.ToTable("ExternalIdentities");
            entity.HasKey(ei => ei.IdentityId);

            entity.Property(ei => ei.Provider).IsRequired().HasMaxLength(128);
            entity.Property(ei => ei.ProviderSubject).IsRequired().HasMaxLength(256);

            entity.HasIndex(ei => new { ei.Provider, ei.ProviderSubject }).IsUnique();

            entity.HasOne(ei => ei.User)
                .WithMany(u => u.ExternalIdentities)
                .HasForeignKey(ei => ei.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(entity => {
            entity.ToTable("RefreshTokens");
            entity.HasKey(rt => rt.TokenId);

            entity.Property(rt => rt.TokenHash).IsRequired().HasMaxLength(128);
            entity.Property(rt => rt.ExpiresAt).IsRequired();

            entity.HasIndex(rt => rt.TokenHash).IsUnique();
            entity.HasIndex(rt => new { rt.UserId, rt.RevokedAt });

            entity.HasOne(rt => rt.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ModerationOperation>(entity => {
            entity.ToTable("ModerationOperations");
            entity.HasKey(mo => mo.OperationId);

            entity.Property(mo => mo.TargetStatus).IsRequired().HasConversion<string>().HasMaxLength(32);
            entity.Property(mo => mo.Reason).IsRequired().HasMaxLength(500);
            entity.Property(mo => mo.RequestFingerprint).IsRequired().HasMaxLength(128);
            entity.Property(mo => mo.ResultVersion).IsRequired();
            entity.Property(mo => mo.CreatedAt).IsRequired();

            entity.HasIndex(mo => mo.TargetUserId);

            entity.HasOne(mo => mo.TargetUser)
                .WithMany()
                .HasForeignKey(mo => mo.TargetUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

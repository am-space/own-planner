using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Users;

namespace OwnPlanner.Infrastructure.Persistence;

/// <summary>
/// Database context for authentication and user management.
/// This context is separate from AppDbContext to isolate auth data.
/// </summary>
public class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
	public DbSet<User> Users => Set<User>();
	public DbSet<PersonalAccessToken> PersonalAccessTokens => Set<PersonalAccessToken>();
	public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
	public DbSet<UserDailyUsage> UserDailyUsages => Set<UserDailyUsage>();
	public DbSet<UserQuotaOverride> UserQuotaOverrides => Set<UserQuotaOverride>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		// User configuration
		var user = modelBuilder.Entity<User>();
		user.HasKey(u => u.Id);
		user.Property(u => u.Email).IsRequired().HasMaxLength(256);
		user.Property(u => u.Username).IsRequired().HasMaxLength(50);
		user.Property(u => u.PasswordHash).IsRequired();
		user.Property(u => u.IsActive).IsRequired();
		user.Property(u => u.CreatedAt).IsRequired();
		user.Property(u => u.UpdatedAt).IsRequired();
		user.Property(u => u.LastLoginAt);
		
		// Create unique index for email only (username is not unique)
		user.HasIndex(u => u.Email).IsUnique();

		var personalAccessToken = modelBuilder.Entity<PersonalAccessToken>();
		personalAccessToken.HasKey(t => t.Id);
		personalAccessToken.Property(t => t.UserId).IsRequired();
		personalAccessToken.Property(t => t.Name).IsRequired().HasMaxLength(100);
		personalAccessToken.Property(t => t.TokenHash).IsRequired().HasMaxLength(128);
		personalAccessToken.Property(t => t.CreatedAt).IsRequired();
		personalAccessToken.Property(t => t.LastUsedAt);
		personalAccessToken.Property(t => t.RevokedAt);
		personalAccessToken.HasOne<User>()
			.WithMany()
			.HasForeignKey(t => t.UserId)
			.OnDelete(DeleteBehavior.Cascade);
		personalAccessToken.HasIndex(t => t.TokenHash).IsUnique();
		personalAccessToken.HasIndex(t => t.UserId);

		var passwordResetToken = modelBuilder.Entity<PasswordResetToken>();
		passwordResetToken.HasKey(t => t.Id);
		passwordResetToken.Property(t => t.UserId).IsRequired();
		passwordResetToken.Property(t => t.TokenHash).IsRequired().HasMaxLength(128);
		passwordResetToken.Property(t => t.ExpiresAt).IsRequired();
		passwordResetToken.Property(t => t.ConsumedAt);
		passwordResetToken.Property(t => t.CreatedAt).IsRequired();
		passwordResetToken.HasOne<User>()
			.WithMany()
			.HasForeignKey(t => t.UserId)
			.OnDelete(DeleteBehavior.Cascade);
		passwordResetToken.HasIndex(t => t.TokenHash).IsUnique();
		passwordResetToken.HasIndex(t => t.UserId);

		var dailyUsage = modelBuilder.Entity<UserDailyUsage>();
		dailyUsage.HasKey(u => u.Id);
		dailyUsage.Property(u => u.UserId).IsRequired();
		dailyUsage.Property(u => u.Date).IsRequired();
		dailyUsage.Property(u => u.RequestCount).IsRequired();
		dailyUsage.Property(u => u.InputTokens).IsRequired();
		dailyUsage.Property(u => u.OutputTokens).IsRequired();
		dailyUsage.Property(u => u.CreatedAt).IsRequired();
		dailyUsage.Property(u => u.UpdatedAt).IsRequired();
		dailyUsage.HasOne<User>()
			.WithMany()
			.HasForeignKey(u => u.UserId)
			.OnDelete(DeleteBehavior.Cascade);
		// One row per user per day; also the conflict target for the atomic increment upsert.
		dailyUsage.HasIndex(u => new { u.UserId, u.Date }).IsUnique();

		var quotaOverride = modelBuilder.Entity<UserQuotaOverride>();
		quotaOverride.HasKey(o => o.Id);
		quotaOverride.Property(o => o.UserId).IsRequired();
		quotaOverride.Property(o => o.DailyRequestLimit);
		quotaOverride.Property(o => o.BurstRequestsPerMinute);
		quotaOverride.Property(o => o.CreatedAt).IsRequired();
		quotaOverride.Property(o => o.UpdatedAt).IsRequired();
		quotaOverride.HasOne<User>()
			.WithMany()
			.HasForeignKey(o => o.UserId)
			.OnDelete(DeleteBehavior.Cascade);
		quotaOverride.HasIndex(o => o.UserId).IsUnique();
	}
}

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
	}
}

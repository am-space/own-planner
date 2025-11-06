using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
	public DbSet<TaskItem> TaskItems => Set<TaskItem>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		var task = modelBuilder.Entity<TaskItem>();
		task.HasKey(t => t.Id);
		task.Property(t => t.Title).IsRequired().HasMaxLength(256);
		task.Property(t => t.Description);
		task.Property(t => t.IsCompleted);
		task.Property(t => t.CreatedAt);
		task.Property(t => t.UpdatedAt);
		task.Property(t => t.DueAt);
		task.Property(t => t.CompletedAt);
	}
}

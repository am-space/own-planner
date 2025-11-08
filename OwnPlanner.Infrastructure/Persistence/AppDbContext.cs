using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
	public DbSet<TaskItem> TaskItems => Set<TaskItem>();
	public DbSet<TaskList> TaskLists => Set<TaskList>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		// TaskItem configuration
		var task = modelBuilder.Entity<TaskItem>();
		task.HasKey(t => t.Id);
		task.Property(t => t.Title).IsRequired().HasMaxLength(256);
		task.Property(t => t.Description);
		task.Property(t => t.IsCompleted);
		task.Property(t => t.CreatedAt);
		task.Property(t => t.UpdatedAt);
		task.Property(t => t.DueAt);
		task.Property(t => t.CompletedAt);
		task.Property(t => t.TaskListId);
		task.HasIndex(t => t.TaskListId);

		// TaskList configuration
		var taskList = modelBuilder.Entity<TaskList>();
		taskList.HasKey(tl => tl.Id);
		taskList.Property(tl => tl.Title).IsRequired().HasMaxLength(256);
		taskList.Property(tl => tl.Description);
		taskList.Property(tl => tl.Color).HasMaxLength(50);
		taskList.Property(tl => tl.IsArchived);
		taskList.Property(tl => tl.CreatedAt);
		taskList.Property(tl => tl.UpdatedAt);

		// Configure relationship - TaskList to TaskItems (one-to-many)
		// When a TaskList is deleted, set TaskListId to null (orphan tasks)
		taskList.HasMany<TaskItem>()
			.WithOne()
			.HasForeignKey(t => t.TaskListId)
			.OnDelete(DeleteBehavior.SetNull);
	}
}

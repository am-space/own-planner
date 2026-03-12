using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Domain.Notes;

namespace OwnPlanner.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
	public DbSet<TaskItem> TaskItems => Set<TaskItem>();
	public DbSet<TaskList> TaskLists => Set<TaskList>();
	public DbSet<NoteList> NoteLists => Set<NoteList>();
	public DbSet<NoteItem> NoteItems => Set<NoteItem>();
	public DbSet<Goal> Goals => Set<Goal>();
	public DbSet<PlanningContext> PlanningContexts => Set<PlanningContext>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		// TaskItem configuration
		var task = modelBuilder.Entity<TaskItem>();
		task.HasKey(t => t.Id);
		task.Property(t => t.Title).IsRequired().HasMaxLength(256);
		task.Property(t => t.Description);
		task.Property(t => t.IsCompleted);
		task.Property(t => t.IsImportant); // Added
		task.Property(t => t.CreatedAt);
		task.Property(t => t.UpdatedAt);
		task.Property(t => t.DueAt);
		task.Property(t => t.CompletedAt);
		task.Property(t => t.TaskListId).IsRequired();
		task.Property(t => t.FocusAt); // My Day feature: nullable focus date
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
		// When a TaskList is deleted, cascade delete all associated TaskItems
		taskList.HasMany<TaskItem>()
			.WithOne()
			.HasForeignKey(t => t.TaskListId)
			.OnDelete(DeleteBehavior.Cascade);

		// NoteList configuration
		var noteList = modelBuilder.Entity<NoteList>();
		noteList.HasKey(nl => nl.Id);
		noteList.Property(nl => nl.Title).IsRequired().HasMaxLength(256);
		noteList.Property(nl => nl.Description);
		noteList.Property(nl => nl.Color).HasMaxLength(50);
		noteList.Property(nl => nl.IsArchived);
		noteList.Property(nl => nl.CreatedAt);
		noteList.Property(nl => nl.UpdatedAt);

		// NoteItem configuration
		var note = modelBuilder.Entity<NoteItem>();
		note.HasKey(n => n.Id);
		note.Property(n => n.Title).IsRequired().HasMaxLength(256);
		note.Property(n => n.Content);
		note.Property(n => n.IsPinned);
		note.Property(n => n.CreatedAt);
		note.Property(n => n.UpdatedAt);
		note.Property(n => n.NoteListId).IsRequired();
		note.HasIndex(n => n.NoteListId);

		// Configure relationship - NoteList to NoteItems (one-to-many)
		// When a NoteList is deleted, cascade delete all associated NoteItems
		noteList.HasMany<NoteItem>()
			.WithOne()
			.HasForeignKey(n => n.NoteListId)
			.OnDelete(DeleteBehavior.Cascade);

		// Goal configuration
		var goal = modelBuilder.Entity<Goal>();
		goal.HasKey(g => g.Id);
		goal.Property(g => g.Title).IsRequired().HasMaxLength(256);
		goal.Property(g => g.Description);
		goal.Property(g => g.Horizon).IsRequired();
		goal.Property(g => g.TargetPeriod).HasMaxLength(20);
		goal.Property(g => g.TargetDate);
		goal.Property(g => g.Status).IsRequired();
		goal.Property(g => g.Metric);
		goal.Property(g => g.MetricCurrent);
		goal.Property(g => g.CreatedAt);
		goal.Property(g => g.UpdatedAt);

		// PlanningContext configuration
		var planningContext = modelBuilder.Entity<PlanningContext>();
		planningContext.HasKey(c => c.Id);
		planningContext.Property(c => c.Name).IsRequired().HasMaxLength(256);
		planningContext.Property(c => c.Type).IsRequired();
		planningContext.Property(c => c.Description);
		planningContext.Property(c => c.Status).IsRequired();
		planningContext.Property(c => c.Color).HasMaxLength(50);
		planningContext.Property(c => c.CreatedAt);
		planningContext.Property(c => c.UpdatedAt);
	}
}

using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Domain.Notes;

namespace OwnPlanner.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
	public DbSet<TaskItem> TaskItems => Set<TaskItem>();
	public DbSet<TaskList> TaskLists => Set<TaskList>();
	public DbSet<NotesList> NotesLists => Set<NotesList>();
	public DbSet<NoteItem> NoteItems => Set<NoteItem>();

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
		task.Property(t => t.TaskListId).IsRequired();
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

		// NotesList configuration
		var notesList = modelBuilder.Entity<NotesList>();
		notesList.HasKey(nl => nl.Id);
		notesList.Property(nl => nl.Title).IsRequired().HasMaxLength(256);
		notesList.Property(nl => nl.Description);
		notesList.Property(nl => nl.Color).HasMaxLength(50);
		notesList.Property(nl => nl.IsArchived);
		notesList.Property(nl => nl.CreatedAt);
		notesList.Property(nl => nl.UpdatedAt);

		// NoteItem configuration
		var note = modelBuilder.Entity<NoteItem>();
		note.HasKey(n => n.Id);
		note.Property(n => n.Title).IsRequired().HasMaxLength(256);
		note.Property(n => n.Content);
		note.Property(n => n.IsPinned);
		note.Property(n => n.CreatedAt);
		note.Property(n => n.UpdatedAt);
		note.Property(n => n.NotesListId).IsRequired();
		note.HasIndex(n => n.NotesListId);

		// Configure relationship - NotesList to NoteItems (one-to-many)
		// When a NotesList is deleted, cascade delete all associated NoteItems
		notesList.HasMany<NoteItem>()
			.WithOne()
			.HasForeignKey(n => n.NotesListId)
			.OnDelete(DeleteBehavior.Cascade);
	}
}

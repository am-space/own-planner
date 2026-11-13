using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Infrastructure.WeeklyReviews;
using OwnPlanner.Infrastructure.Tasks;

namespace OwnPlanner.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
	public DbSet<WeeklyReviewPreferencesRow> WeeklyReviewPreferences => Set<WeeklyReviewPreferencesRow>();
	public DbSet<WeeklyReviewRow> WeeklyReviews => Set<WeeklyReviewRow>();
	public DbSet<TaskItem> TaskItems => Set<TaskItem>();
	public DbSet<TaskOccurrenceFactRow> TaskOccurrenceFacts => Set<TaskOccurrenceFactRow>();
	public DbSet<RecurringSuccessorOperationRow> RecurringSuccessorOperations => Set<RecurringSuccessorOperationRow>();
	public DbSet<TaskList> TaskLists => Set<TaskList>();
	public DbSet<NoteList> NoteLists => Set<NoteList>();
	public DbSet<NoteItem> NoteItems => Set<NoteItem>();
	public DbSet<Goal> Goals => Set<Goal>();
	public DbSet<PlanningContext> PlanningContexts => Set<PlanningContext>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		var preferences = modelBuilder.Entity<WeeklyReviewPreferencesRow>();
		preferences.ToTable("WeeklyReviewPreferences");
		preferences.HasKey(p => p.Id);
		preferences.Property(p => p.Id).ValueGeneratedNever();
		var review = modelBuilder.Entity<WeeklyReviewRow>();
		review.ToTable("WeeklyReviews");
		review.HasKey(r => r.Id);
		review.HasIndex(r => r.TargetWeek).IsUnique();

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
		task.Property(t => t.ActiveTaskListId);
		task.Property(t => t.TrashedAt);
		task.Property(t => t.FocusAt); // My Day feature: nullable focus date
		task.Property(t => t.GoalId); // Soft reference to a Goal
		task.Property(t => t.Recurrence).HasConversion(
			schedule => RecurrenceStorage.Serialize(schedule!), json => RecurrenceStorage.Deserialize(json));
		task.Property(t => t.RecurrenceCheckedBefore);
		task.Property(t => t.RecurrenceCalendarKey);
		task.Property(t => t.RecurrenceRevision).IsConcurrencyToken();
		task.HasIndex(t => t.RecurrencePredecessorTaskId).IsUnique();
		task.HasIndex(t => t.RecurrenceSuccessorTaskId).IsUnique();

		var successorOperation = modelBuilder.Entity<RecurringSuccessorOperationRow>();
		successorOperation.ToTable("RecurringSuccessorOperations");
		successorOperation.HasKey(o => o.RequestId);
		successorOperation.Property(o => o.RequestFingerprint).IsRequired().HasMaxLength(64);
		successorOperation.HasIndex(o => o.SourceTaskId).IsUnique();
		successorOperation.HasIndex(o => o.SuccessorTaskId).IsUnique();
		successorOperation.HasOne<TaskItem>().WithMany().HasForeignKey(o => o.SourceTaskId).OnDelete(DeleteBehavior.Cascade);
		successorOperation.HasOne<TaskItem>().WithMany().HasForeignKey(o => o.SuccessorTaskId).OnDelete(DeleteBehavior.Cascade);

		var fact = modelBuilder.Entity<TaskOccurrenceFactRow>();
		fact.ToTable("TaskOccurrenceFacts", table => table.HasCheckConstraint("CK_TaskOccurrenceFacts_Status_CompletedAt",
			"(Status = 0 AND CompletedAt IS NOT NULL) OR (Status IN (1, 2) AND CompletedAt IS NULL)"));
		fact.HasKey(f => new { f.TaskId, f.OccurrenceDate });
		fact.Property(f => f.CompletedAt).HasConversion(
			value => value.HasValue ? value.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture) : null,
			value => value != null ? DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind) : null);
		fact.HasIndex(f => f.CompletedAt);
		fact.HasOne<TaskItem>().WithMany().HasForeignKey(f => f.TaskId).OnDelete(DeleteBehavior.Cascade);
		task.HasIndex(t => t.TaskListId);
		task.HasIndex(t => t.ActiveTaskListId);
		task.HasIndex(t => t.TrashedAt);
		task.HasIndex(t => t.GoalId);

		// TaskList configuration
		var taskList = modelBuilder.Entity<TaskList>();
		taskList.HasKey(tl => tl.Id);
		taskList.Property(tl => tl.Title).IsRequired().HasMaxLength(256);
		taskList.Property(tl => tl.Description);
		taskList.Property(tl => tl.Color).HasMaxLength(50);
		taskList.Property(tl => tl.IsArchived);
		taskList.Property(tl => tl.IsSystem);
		taskList.Property(tl => tl.ContextId);
		taskList.Property(tl => tl.CreatedAt);
		taskList.Property(tl => tl.UpdatedAt);
		taskList.HasIndex(tl => tl.ContextId);

		// Active tasks are cascade-deleted with their list. Trashed tasks clear ActiveTaskListId and
		// retain TaskListId only as the soft restore destination, so they survive list deletion.
		taskList.HasMany<TaskItem>()
			.WithOne()
			.HasForeignKey(t => t.ActiveTaskListId)
			.OnDelete(DeleteBehavior.Cascade);

		// NoteList configuration
		var noteList = modelBuilder.Entity<NoteList>();
		noteList.HasKey(nl => nl.Id);
		noteList.Property(nl => nl.Title).IsRequired().HasMaxLength(256);
		noteList.Property(nl => nl.Description);
		noteList.Property(nl => nl.Color).HasMaxLength(50);
		noteList.Property(nl => nl.IsArchived);
		noteList.Property(nl => nl.IsSystem);
		noteList.Property(nl => nl.ContextId);
		noteList.Property(nl => nl.CreatedAt);
		noteList.Property(nl => nl.UpdatedAt);
		noteList.HasIndex(nl => nl.ContextId);

		// NoteItem configuration
		var note = modelBuilder.Entity<NoteItem>();
		note.HasKey(n => n.Id);
		note.Property(n => n.Title).IsRequired().HasMaxLength(256);
		note.Property(n => n.Content);
		note.Property(n => n.IsPinned);
		note.Property(n => n.CreatedAt);
		note.Property(n => n.UpdatedAt);
		note.Property(n => n.NoteListId).IsRequired();
		note.Property(n => n.GoalId); // Soft reference to a Goal
		note.HasIndex(n => n.NoteListId);
		note.HasIndex(n => n.GoalId);

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
		goal.Property(g => g.PausedAt);
		goal.Property(g => g.LastResumedAt);
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

	public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
	{
		var revisions = CaptureRecurrenceRevisions();
		try
		{
			foreach (var revision in revisions) revision.Advance();
			return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			foreach (var revision in revisions) revision.Restore();
			throw;
		}
	}

	public override int SaveChanges(bool acceptAllChangesOnSuccess)
	{
		var revisions = CaptureRecurrenceRevisions();
		try
		{
			foreach (var revision in revisions) revision.Advance();
			return base.SaveChanges(acceptAllChangesOnSuccess);
		}
		catch
		{
			foreach (var revision in revisions) revision.Restore();
			throw;
		}
	}

	private RecurrenceRevisionSnapshot[] CaptureRecurrenceRevisions() => ChangeTracker.Entries<TaskItem>()
		.Where(e => e.State == EntityState.Modified && e.Entity.Recurrence is not null)
		.Select(e => e.Property(t => t.RecurrenceRevision))
		.Select(property => new RecurrenceRevisionSnapshot(property, property.CurrentValue, property.IsModified))
		.ToArray();

	private sealed record RecurrenceRevisionSnapshot(PropertyEntry<TaskItem, long> Property, long Value, bool WasModified)
	{
		public void Advance() => Property.CurrentValue = checked(Property.OriginalValue + 1);

		public void Restore()
		{
			// A detached instance can be attached to a fresh context on retry. Never leave it
			// carrying a revision that was not successfully saved, even after cancellation.
			Property.IsModified = WasModified;
			Property.CurrentValue = Value;
		}
	}
}

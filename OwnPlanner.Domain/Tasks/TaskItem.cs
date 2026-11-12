namespace OwnPlanner.Domain.Tasks;

public class TaskItem : EntityBase
{
	public string Title { get; private set; } = string.Empty;
	public string? Description { get; private set; }
	public bool IsCompleted { get; private set; }
	public bool IsImportant { get; private set; }
	public DateTime? DueAt { get; private set; }
	public DateTime? CompletedAt { get; private set; }
	public Guid TaskListId { get; private set; }
	/// <summary>
	/// Database relationship used only while the task is active. Trashing clears this relationship so
	/// deleting the original list cannot cascade-delete a recoverable task; <see cref="TaskListId"/>
	/// continues to remember the restore destination.
	/// </summary>
	public Guid? ActiveTaskListId { get; private set; }
	public DateTime? TrashedAt { get; private set; }
	public DateTime? FocusAt { get; private set; } // My Day feature: nullable focus date
	/// <summary>Optional soft reference to a Goal. No FK constraint — stale references are acceptable.</summary>
	public Guid? GoalId { get; private set; }
	/// <summary>Immutable series configuration. Null retains ordinary-task behavior.</summary>
	public RecurrenceSchedule? Recurrence { get; private set; }
	/// <summary>Exclusive prefix processed under RecurrenceCalendarKey; not occurrence history.</summary>
	public DateOnly? RecurrenceCheckedBefore { get; private set; }
	public string? RecurrenceCalendarKey { get; private set; }
	/// <summary>Persistence concurrency token protecting detached series writes and checkpoint changes.</summary>
	public long RecurrenceRevision { get; private set; }

	/// <summary>Configures a new, active ordinary task as a series. Schedule edits require a successor.</summary>
	public void ConfigureRecurrence(RecurrenceSchedule schedule)
	{
		ArgumentNullException.ThrowIfNull(schedule);
		if (Recurrence is not null || IsCompleted || TrashedAt.HasValue)
			throw new InvalidOperationException("Only a new active series can be configured; use an explicit successor for schedule changes.");
		Recurrence = schedule;
		Touch();
	}

	/// <summary>Records an atomic reconciliation prefix without changing user-visible update ordering.</summary>
	public void CheckRecurrenceBefore(DateOnly date, string calendarKey)
	{
		if (Recurrence is null) throw new InvalidOperationException("Task is not recurring.");
		if (date < Recurrence.StartDate) throw new ArgumentOutOfRangeException(nameof(date));
		ArgumentException.ThrowIfNullOrWhiteSpace(calendarKey);
		RecurrenceCheckedBefore = date;
		RecurrenceCalendarKey = calendarKey;
	}

	// EF Core constructor
	private TaskItem() { }

	public TaskItem(string title, Guid taskListId, string? description = null, DateTime? dueAt = null, bool isImportant = false, Guid? goalId = null)
		: base(Guid.NewGuid())
	{
		SetTitle(title);
		TaskListId = taskListId;
		ActiveTaskListId = taskListId;
		SetDescription(description);
		SetDueAt(dueAt);
		IsImportant = isImportant;
		FocusAt = null;
		GoalId = goalId;
	}

	public void SetTitle(string title)
	{
		if (string.IsNullOrWhiteSpace(title))
			throw new ArgumentException("Title is required", nameof(title));
		Title = title.Trim();
		Touch();
	}

	public void SetDescription(string? description)
	{
		Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
		Touch();
	}

	public void SetDueAt(DateTime? dueAt)
	{
		DueAt = dueAt.HasValue ? DateTime.SpecifyKind(dueAt.Value, DateTimeKind.Utc) : null;
		Touch();
	}

	public void SetImportant(bool isImportant)
	{
		IsImportant = isImportant;
		Touch();
	}

	public void SetFocusAt(DateTime? focusAt)
	{
		FocusAt = focusAt.HasValue ? DateTime.SpecifyKind(focusAt.Value, DateTimeKind.Utc) : null;
		Touch();
	}

	public void ClearFocusAt()
	{
		FocusAt = null;
		Touch();
	}

	public void SetGoalId(Guid? goalId)
	{
		GoalId = goalId;
		Touch();
	}

	public void Complete()
	{
		if (!IsCompleted)
		{
			IsCompleted = true;
			// A series ends administratively; only occurrence facts represent performed work.
			CompletedAt = Recurrence is null ? DateTime.UtcNow : null;
			Touch();
		}
	}

	public void Reopen()
	{
		if (Recurrence is not null)
			throw new InvalidOperationException("Resume a recurring series with an explicit successor and start date.");
		if (IsCompleted)
		{
			IsCompleted = false;
			CompletedAt = null;
			Touch();
		}
	}

	public void AssignToList(Guid taskListId)
	{
		if (TrashedAt.HasValue)
			throw new InvalidOperationException("A trashed task must be restored before it can be assigned.");
		TaskListId = taskListId;
		ActiveTaskListId = taskListId;
		Touch();
	}

	public void Trash()
	{
		if (TrashedAt.HasValue)
			return;

		TrashedAt = DateTime.UtcNow;
		if (Recurrence is not null) Complete();
		ActiveTaskListId = null;
		Touch();
	}

	public void Restore()
	{
		if (!TrashedAt.HasValue)
			return;

		TrashedAt = null;
		ActiveTaskListId = TaskListId;
		Touch();
	}


}

using FluentAssertions;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Domain.Tests.Tasks;

public sealed class RecurringTaskItemTests
{
	private static TaskItem Series()
	{
		var task = new TaskItem("Train", Guid.NewGuid());
		task.ConfigureRecurrence(new(RecurrenceRule.Monthly(1, [31]), new(2026, 1, 1), null, MissedOccurrencePolicy.RetainEachObligation));
		return task;
	}

	[Fact]
	public void SuccessorCopiesCommonFieldsAndLinksWithoutCompletingOrRewritingTheOldRule()
	{
		var old = Series();
		old.SetDescription("Details"); old.SetImportant(true); old.SetGoalId(Guid.NewGuid());
		old.SetDueAt(DateTime.UtcNow); old.SetFocusAt(DateTime.UtcNow);
		var original = old.Recurrence!;
		var schedule = new RecurrenceSchedule(RecurrenceRule.Weekly(2, [DayOfWeek.Monday], DayOfWeek.Sunday),
			new(2026, 2, 1), null, MissedOccurrencePolicy.SkipExpiredPeriods);
		var successor = old.CreateRecurrenceSuccessor(schedule);
		old.Recurrence!.Rule.Should().Be(original.Rule);
		old.Recurrence.StartDate.Should().Be(original.StartDate);
		old.Recurrence.EndDate.Should().Be(new DateOnly(2026, 1, 31));
		old.IsCompleted.Should().BeFalse();
		successor.Recurrence.Should().Be(schedule);
		successor.Title.Should().Be(old.Title); successor.Description.Should().Be(old.Description);
		successor.TaskListId.Should().Be(old.TaskListId); successor.GoalId.Should().Be(old.GoalId);
		successor.IsImportant.Should().BeTrue(); successor.DueAt.Should().BeNull(); successor.FocusAt.Should().BeNull();
		successor.RecurrencePredecessorTaskId.Should().Be(old.Id); old.RecurrenceSuccessorTaskId.Should().Be(successor.Id);
		Action sibling = () => old.CreateRecurrenceSuccessor(schedule);
		sibling.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void RetiringBeforeTheOriginalStartIsExplicitlyEmptyAndDoesNotRelaxCreationValidation()
	{
		var old = Series();
		var successor = old.CreateRecurrenceSuccessor(new(old.Recurrence!.Rule, new(2025, 12, 1), null, old.Recurrence.MissedPolicy));
		old.Recurrence.IsEmpty.Should().BeTrue(); old.Recurrence.EndDate.Should().Be(new DateOnly(2025, 11, 30));
		successor.Recurrence!.IsEmpty.Should().BeFalse();
		Action invalidCreation = () => new RecurrenceSchedule(old.Recurrence.Rule, new(2026, 1, 1), new(2025, 11, 30), old.Recurrence.MissedPolicy);
		invalidCreation.Should().Throw<ArgumentException>();
		Action invalidSuccessor = () => successor.CreateRecurrenceSuccessor(old.Recurrence);
		invalidSuccessor.Should().Throw<ArgumentException>();
		var ordinary = new TaskItem("New series", Guid.NewGuid());
		Action invalidConfiguration = () => ordinary.ConfigureRecurrence(old.Recurrence);
		invalidConfiguration.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void ResumptionPreservesOldCompletionAndNeverExtendsAnEarlierEnd()
	{
		var old = Series(); old.Complete();
		var earlier = old.Recurrence!.ShortenBefore(new(2026, 2, 1));
		earlier.ShortenBefore(new(2026, 3, 1)).EndDate.Should().Be(new DateOnly(2026, 1, 31));
		var successor = old.CreateRecurrenceSuccessor(new(old.Recurrence.Rule, new(2026, 3, 1), null, old.Recurrence.MissedPolicy));
		old.IsCompleted.Should().BeTrue(); old.CompletedAt.Should().BeNull(); successor.IsCompleted.Should().BeFalse();
	}

	[Fact]
	public void AdministrativeCloseIsIdempotentAndDoesNotClaimPerformedWork()
	{
		var task = Series();
		task.Complete();
		var updated = task.UpdatedAt;
		task.Complete();
		task.IsCompleted.Should().BeTrue();
		task.CompletedAt.Should().BeNull();
		task.UpdatedAt.Should().Be(updated);
		var reopen = () => task.Reopen();
		reopen.Should().Throw<InvalidOperationException>().WithMessage("*successor*");
	}

	[Fact]
	public void TrashRestorePreservesScheduleAndClosureWithoutResumingInactiveDates()
	{
		var task = Series();
		var rule = task.Recurrence;
		task.Trash();
		task.Restore();
		task.Recurrence.Should().Be(rule);
		task.IsCompleted.Should().BeTrue();
		task.CompletedAt.Should().BeNull();
		task.TrashedAt.Should().BeNull();
	}

	[Fact]
	public void CheckpointsDoNotReorderTheUsersTaskListAndCannotPrecedeStart()
	{
		var task = Series();
		var updated = task.UpdatedAt;
		task.CheckRecurrenceBefore(new(2026, 2, 1), "UTC|1");
		task.UpdatedAt.Should().Be(updated);
		task.RecurrenceCheckedBefore.Should().Be(new DateOnly(2026, 2, 1));
		Action invalid = () => task.CheckRecurrenceBefore(new(2025, 12, 31), "UTC|1");
		invalid.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Fact]
	public void ConfigurationCannotRewriteAnExistingSeriesOrConfigureAnInactiveTask()
	{
		var task = Series();
		Action change = () => task.ConfigureRecurrence(task.Recurrence!);
		change.Should().Throw<InvalidOperationException>();
		var ordinary = new TaskItem("Done", Guid.NewGuid());
		ordinary.Complete();
		Action inactive = () => ordinary.ConfigureRecurrence(task.Recurrence!);
		inactive.Should().Throw<InvalidOperationException>();
		ordinary.CompletedAt.Should().NotBeNull();
		ordinary.Reopen();
		ordinary.IsCompleted.Should().BeFalse();
	}
}

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

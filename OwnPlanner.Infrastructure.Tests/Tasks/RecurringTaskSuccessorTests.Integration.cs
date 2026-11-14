using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Infrastructure.Tests.Tasks;

public sealed partial class RecurringTaskSuccessorTests
{
	[Fact]
	public async Task MonthlyDowntimeClosureCorrectionAndRestartedResume_PreserveDatesTimesAndAnEmptyBreak()
	{
		_clock.Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
		var source = await Seed(MissedOccurrencePolicy.SkipExpiredPeriods, new(2026, 1, 1), new(2026, 2, 28),
			RecurrenceRule.Monthly(1, [29, 30, 31]));
		// Three January dates; February clamps all three to one obligation.
		foreach (var day in new[] { 29, 30, 31 })
			await Lifecycle.CompleteOccurrenceAsync(new(source.Id, new(2026, 1, day)), _clock.Now, Ct);
		_clock.Now = new(2026, 3, 5, 12, 0, 0, DateTimeKind.Utc); // host was down for the February expiry
		await Lifecycle.ReconcileAsync(Ct); await Lifecycle.ReconcileAsync(Ct);
		var old = await Get(source.Id); old.IsCompleted.Should().BeTrue(); old.CompletedAt.Should().BeNull();
		var feb = (await Facts()).Single(f => f.Id.OccurrenceDate == new DateOnly(2026, 2, 28));
		feb.Status.Should().Be(TaskOccurrenceStatus.Skipped); feb.CompletedAt.Should().BeNull();
		var actual = _clock.Now.AddDays(-4);
		await Lifecycle.CompleteOccurrenceAsync(feb.Id, actual, Ct);
		await Lifecycle.CompleteOccurrenceAsync(feb.Id, ct: Ct);
		(await Get(source.Id)).IsCompleted.Should().BeTrue();
		// Every service call opens a fresh migrated SQLite context, including after catch-up/correction.
		var request = new RecurringSuccessorRequest(RecurringSuccessorKind.Resume, new(2026, 4, 30));
		var preview = await Successors.PreviewAsync(source.Id, request, Ct);
		var requestId = Guid.NewGuid();
		var result = await Successors.ApplyAsync(source.Id, requestId, request, preview.PreviewToken, ct: Ct);
		(await Successors.ApplyAsync(source.Id, requestId, request, preview.PreviewToken, ct: Ct)).Should().Be(result);
		var next = await Get(result.SuccessorTaskId); next.Recurrence!.EndDate.Should().BeNull();
		next.Recurrence.StartDate.Should().Be(new DateOnly(2026, 4, 30));
		_clock.Now = new(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc);
		await Lifecycle.ReconcileAsync(Ct);
		var all = await Facts(); all.Should().HaveCount(4);
		all.Should().OnlyContain(f => f.Id.TaskId == source.Id && f.Status == TaskOccurrenceStatus.Completed);
		all.Single(f => f.Id == feb.Id).CompletedAt.Should().Be(actual);
		all.Where(f => f.Id.OccurrenceDate.Month == 1).Should().OnlyContain(f => f.CompletedAt == new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc));
		(await Get(source.Id)).CompletedAt.Should().BeNull(); (await Get(next.Id)).IsCompleted.Should().BeFalse();
		await using var db = await _factory.CreateAsync(Ct);
		(await db.TaskItems.CountAsync(Ct)).Should().Be(2);
	}
}

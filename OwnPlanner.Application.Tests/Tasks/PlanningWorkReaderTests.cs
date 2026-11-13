using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tests.Tasks;

public sealed class PlanningWorkReaderTests
{
	private static OccurrenceDateRange Range => new(new(2026, 11, 9), new(2026, 11, 23));
	private CancellationToken Ct => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData("series")]
	[InlineData("ordinary")]
	[InlineData("unknown")]
	[InlineData("all")]
	public async Task TaskSelectionBoundsFactReadsAndAvoidsEnumeratingUnrelatedRetainedHistoryAcrossPages(string selection)
	{
		var unrelatedSchedule = new RecurrenceSchedule(RecurrenceRule.Weekly(1, Enum.GetValues<DayOfWeek>(), DayOfWeek.Monday),
			new(1990, 1, 1), null, MissedOccurrencePolicy.RetainEachObligation);
		var selectedSchedule = new RecurrenceSchedule(RecurrenceRule.Weekly(1, [DayOfWeek.Friday], DayOfWeek.Monday),
			new(2026, 11, 6), null, MissedOccurrencePolicy.RetainEachObligation);
		var unrelated = Source(unrelatedSchedule);
		var selected = Source(selection == "ordinary" ? null : selectedSchedule);
		Guid? taskId = selection == "all" ? null : selection == "unknown" ? Guid.NewGuid() : selected.TaskId;
		var facts = new[] { new TaskOccurrenceFact(new(selected.TaskId, selectedSchedule.StartDate), TaskOccurrenceStatus.Cancelled) };
		var snapshot = Substitute.For<IPlanningWorkSnapshot>();
		snapshot.GetSourcesAsync(false, Ct).Returns(new[] { unrelated, selected });
		snapshot.GetScheduledFactsAsync(Arg.Any<OccurrenceDateRange>(), Arg.Any<Guid?>(), Ct).Returns(call =>
			facts.Where(f => (!call.ArgAt<Guid?>(1).HasValue || f.Id.TaskId == call.ArgAt<Guid?>(1))
				&& f.Id.OccurrenceDate >= call.ArgAt<OccurrenceDateRange>(0).StartDate
				&& f.Id.OccurrenceDate < call.ArgAt<OccurrenceDateRange>(0).EndExclusiveDate).ToArray());
		var store = Substitute.For<IPlanningWorkStore>();
		store.ReadAsync(Arg.Any<Func<IPlanningWorkSnapshot, CancellationToken, Task<PlanningWorkPage>>>(), Ct)
			.Returns(call => call.ArgAt<Func<IPlanningWorkSnapshot, CancellationToken, Task<PlanningWorkPage>>>(0)(snapshot, Ct));
		var calendar = Substitute.For<IPlanningCalendar>();
		calendar.ResolveAsync("today", Ct).Returns(PlanningCalendarResolver.Resolve("today", new(2026, 11, 15, 18, 0, 0, DateTimeKind.Utc), "UTC"));
		var calculator = Substitute.For<IRecurrenceCalculator>();
		calculator.Enumerate(Arg.Any<RecurrenceSchedule>(), Arg.Any<OccurrenceDateRange>()).Returns(call =>
			new RecurrenceCalculator().Enumerate(call.ArgAt<RecurrenceSchedule>(0), call.ArgAt<OccurrenceDateRange>(1)));
		var reader = new PlanningWorkReader(store, calendar, calculator);
		var total = selection switch
		{
			"all" => Range.EndExclusiveDate.DayNumber - unrelatedSchedule.StartDate.DayNumber + 2,
			"series" => 2,
			"ordinary" => 1,
			_ => 0
		};
		for (var offset = 0; offset < 2; offset++)
		{
			var page = await reader.QueryAsync(new(Range, IncludeRetainedBeforeStart: true, TaskId: taskId, Offset: offset, Limit: 1), Ct);
			page.TotalCount.Should().Be(total);
			page.Items.Count.Should().Be(offset < total ? 1 : 0);
			page.HasMore.Should().Be(offset + page.Items.Count < total);
			if (taskId.HasValue) page.Items.Should().OnlyContain(row => row.TaskId == taskId);
			if (selection == "series")
			{
				page.Counts.Should().Be(new PlanningWorkCounts(0, 1, 2));
				page.Items.Single().OccurrenceDate.Should().Be(new DateOnly(2026, 11, 13).AddDays(offset * 7));
			}
			if (selection == "ordinary") page.Counts.Should().Be(new PlanningWorkCounts(1, 0, 0));
			if (selection == "all") page.Counts.Should().Be(new PlanningWorkCounts(0, 2, total));
		}
		var expectedStart = selection == "all" ? unrelatedSchedule.StartDate : selection == "series" ? selectedSchedule.StartDate : Range.StartDate;
		await snapshot.Received(2).GetScheduledFactsAsync(Arg.Is<OccurrenceDateRange>(r => r != null && r.StartDate == expectedStart
			&& r.EndExclusiveDate == Range.EndExclusiveDate), taskId, Ct);
		calculator.Received(selection == "all" ? 2 : 0).Enumerate(unrelatedSchedule, Arg.Any<OccurrenceDateRange>());
		calculator.Received(selection is "all" or "series" ? 2 : 0).Enumerate(selectedSchedule, Arg.Any<OccurrenceDateRange>());

		static PlanningWorkSource Source(RecurrenceSchedule? schedule) => new(Guid.NewGuid(), "Task", null,
			Guid.NewGuid(), null, null, false, false, new(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), null, schedule, null, null);
	}

	[Theory]
	[InlineData("range")]
	[InlineData("too-long")]
	[InlineData("negative-offset")]
	[InlineData("zero-limit")]
	[InlineData("large-limit")]
	[InlineData("selection")]
	[InlineData("kind")]
	[InlineData("status")]
	public async Task InvalidPlanningRequestsFailBeforeCalendarOrPersistence(string invalid)
	{
		var store = Substitute.For<IPlanningWorkStore>(); var calendar = Substitute.For<IPlanningCalendar>();
		var reader = new PlanningWorkReader(store, calendar, new RecurrenceCalculator());
		var query = new PlanningWorkQuery(Range);
		query = invalid switch
		{
			"range" => query with { Range = null! },
			"too-long" => query with { Range = new(new(2000, 1, 1), new(2026, 1, 1)) },
			"negative-offset" => query with { Offset = -1 },
			"zero-limit" => query with { Limit = 0 },
			"large-limit" => query with { Limit = 101 },
			"selection" => query with { OrdinaryTasks = (OrdinaryTaskSelection)99 },
			"kind" => query with { Kind = (PlanningWorkKind)99 },
			_ => query with { Status = (PlanningWorkStatus)99 }
		};
		var action = () => reader.QueryAsync(query, Ct);
		await action.Should().ThrowAsync<ArgumentException>();
		store.ReceivedCalls().Should().BeEmpty(); calendar.ReceivedCalls().Should().BeEmpty();
	}

	[Theory]
	[InlineData("basis")]
	[InlineData("missing-dates")]
	[InlineData("both-bases")]
	[InlineData("missing-instants")]
	[InlineData("non-utc")]
	[InlineData("backwards")]
	[InlineData("too-long")]
	[InlineData("page")]
	public async Task InvalidHistorySelectionFailsBeforeCalendarOrPersistence(string invalid)
	{
		var store = Substitute.For<IPlanningWorkStore>(); var calendar = Substitute.For<IPlanningCalendar>();
		var history = new TaskOccurrenceHistory(store, calendar);
		var start = new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc);
		var query = new OccurrenceHistoryQuery(OccurrenceHistoryBasis.CompletionTime, StartUtc: start, EndExclusiveUtc: start.AddDays(14));
		query = invalid switch
		{
			"basis" => query with { Basis = (OccurrenceHistoryBasis)99 },
			"missing-dates" => new(OccurrenceHistoryBasis.ScheduledDate),
			"both-bases" => query with { ScheduledRange = Range },
			"missing-instants" => query with { EndExclusiveUtc = null },
			"non-utc" => query with { StartUtc = DateTime.SpecifyKind(start, DateTimeKind.Unspecified) },
			"backwards" => query with { EndExclusiveUtc = start },
			"too-long" => query with { EndExclusiveUtc = start.AddDays(3661) },
			_ => query with { Offset = -1 }
		};
		var action = () => history.QueryAsync(query, Ct);
		await action.Should().ThrowAsync<ArgumentException>();
		store.ReceivedCalls().Should().BeEmpty(); calendar.ReceivedCalls().Should().BeEmpty();
	}
}

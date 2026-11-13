using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Tasks.Recurrence;

namespace OwnPlanner.Application.Tests.Tasks;

public sealed class PlanningWorkReaderTests
{
	private static OccurrenceDateRange Range => new(new(2026, 11, 9), new(2026, 11, 23));
	private CancellationToken Ct => TestContext.Current.CancellationToken;

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

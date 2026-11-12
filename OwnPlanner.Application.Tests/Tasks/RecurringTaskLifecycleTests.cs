using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tests.Tasks;

public sealed class RecurringTaskLifecycleTests
{
	[Theory]
	[InlineData(DateTimeKind.Local, -1)]
	[InlineData(DateTimeKind.Unspecified, -1)]
	[InlineData(DateTimeKind.Utc, 1)]
	public async Task InvalidReportedTimeFailsBeforeOpeningAWriteTransaction(DateTimeKind kind, int hours)
	{
		var ct = TestContext.Current.CancellationToken;
		var now = new DateTime(2026, 11, 15, 18, 0, 0, DateTimeKind.Utc);
		var calendar = Substitute.For<IPlanningCalendar>();
		calendar.ResolveAsync("today", ct).Returns(PlanningCalendarResolver.Resolve("today", now, "UTC"));
		var store = Substitute.For<IRecurringTaskStore>();
		var service = new RecurringTaskLifecycle(store, calendar, new RecurrenceCalculator());
		var action = () => service.CompleteOccurrenceAsync(new(Guid.NewGuid(), new(2026, 11, 13)), DateTime.SpecifyKind(now.AddHours(hours), kind), ct);
		await action.Should().ThrowAsync<ArgumentException>();
		store.ReceivedCalls().Should().BeEmpty();
		await calendar.Received(1).ResolveAsync("today", ct);
	}
}

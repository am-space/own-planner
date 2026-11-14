using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tests.Tasks;

public sealed class RecurringTaskManagementTests
{
	private readonly ITaskItemRepository _tasks = Substitute.For<ITaskItemRepository>();
	private readonly ITaskListRepository _lists = Substitute.For<ITaskListRepository>();
	private readonly IRecurringTaskStore _store = Substitute.For<IRecurringTaskStore>();
	private readonly IPlanningCalendar _calendar = Substitute.For<IPlanningCalendar>();
	private readonly IRecurringTaskSuccessors _successors = Substitute.For<IRecurringTaskSuccessors>();
	private RecurringTaskManagement Service => new(_tasks, _lists, _store, _calendar, _successors);

	[Fact]
	public async Task CreationInsertsExactlyOneConfiguredParentUsingSharedAlignmentAndCommonFields()
	{
		var ct = TestContext.Current.CancellationToken; var list = new TaskList("Work"); var goal = Guid.NewGuid();
		_lists.GetAsync(list.Id, ct).Returns(list);
		_calendar.ResolveAsync("today", ct).Returns(PlanningCalendarResolver.Resolve("today", new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc), "Asia/Tokyo", 0));
		var result = await Service.CreateAsync("Work", list.Id, new(2026, 11, 14), new(RecurrenceFrequency.Weekly, 2, [DayOfWeek.Friday]),
			MissedOccurrencePolicy.RetainEachObligation, description: "Details", isImportant: true, goalId: goal, ct: ct);
		await _tasks.Received(1).AddAsync(Arg.Is<TaskItem>(t => t != null && t.Recurrence != null && t.Recurrence.Rule.WeekStart == DayOfWeek.Sunday
			&& t.Recurrence.Rule.Interval == 2 && t.Title == "Work" && t.Description == "Details" && t.IsImportant && t.GoalId == goal
			&& !t.IsCompleted && t.FocusAt == null && t.DueAt == null), ct);
		result.Task.IsRecurring.Should().BeTrue();
		result.Schedule.StartDate.Should().Be(new(2026, 11, 14));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	[InlineData(5)]
	public async Task InvalidScheduleOrUnavailableListDoesNotInsertAnOrdinaryPartialTask(int invalid)
	{
		var ct = TestContext.Current.CancellationToken; var list = new TaskList("Work");
		if (invalid == 1) list.Archive();
		if (invalid != 0) _lists.GetAsync(list.Id, ct).Returns(list);
		var rule = invalid switch
		{
			2 => new RecurringRuleInput(RecurrenceFrequency.Monthly, MonthDays: []),
			3 => new RecurringRuleInput(RecurrenceFrequency.Monthly, MonthDays: [32]),
			4 => new RecurringRuleInput(RecurrenceFrequency.Monthly, 0, MonthDays: [1]),
			_ => new RecurringRuleInput(RecurrenceFrequency.Monthly, MonthDays: [1])
		};
		var action = () => Service.CreateAsync("Work", list.Id, new(2026, 11, 14), rule, MissedOccurrencePolicy.SkipExpiredPeriods,
			endDate: invalid == 5 ? new(2026, 11, 13) : null, ct: ct);
		await action.Should().ThrowAsync<Exception>();
		_tasks.ReceivedCalls().Should().BeEmpty();
	}

	[Fact]
	public async Task ConfigureUsesAtomicStoreAndCannotRewriteASeries()
	{
		var ct = TestContext.Current.CancellationToken; var task = new TaskItem("Existing", Guid.NewGuid(), dueAt: DateTime.UtcNow);
		var tx = Substitute.For<IRecurringTaskTransaction>(); tx.Task.Returns(task); tx.ListAvailable.Returns(true);
		_store.ExecuteAsync(task.Id, Arg.Any<Func<IRecurringTaskTransaction, CancellationToken, Task<RecurringTaskDetails>>>(), ct)
			.Returns(call => call.ArgAt<Func<IRecurringTaskTransaction, CancellationToken, Task<RecurringTaskDetails>>>(1)(tx, ct));
		var input = new RecurringRuleInput(RecurrenceFrequency.Monthly, MonthDays: [29, 30, 31]);
		var result = await Service.ConfigureAsync(task.Id, new(2026, 11, 14), input, MissedOccurrencePolicy.RetainEachObligation, ct: ct);
		result.Task.DueAt.Should().Be(task.DueAt);
		var rewrite = () => Service.ConfigureAsync(task.Id, new(2026, 12, 14), input, MissedOccurrencePolicy.SkipExpiredPeriods, ct: ct);
		await rewrite.Should().ThrowAsync<InvalidOperationException>().WithMessage("*successor*");
		task.Recurrence!.StartDate.Should().Be(new(2026, 11, 14));
		_tasks.ReceivedCalls().Should().BeEmpty();
	}

	[Fact]
	public async Task ChangedWeeklySuccessorRequiresStableExplicitAlignmentAcrossPreferenceChanges()
	{
		var ct = TestContext.Current.CancellationToken; var id = Guid.NewGuid(); var request = Guid.NewGuid();
		var missingAlignment = () => Service.PreviewAsync(id, RecurringSuccessorKind.Replace, new(2026, 12, 1),
			new(RecurrenceFrequency.Weekly, Weekdays: [DayOfWeek.Friday]), ct: ct);
		await missingAlignment.Should().ThrowAsync<ArgumentException>().WithMessage("*weekStart*");
		var rule = new RecurringRuleInput(RecurrenceFrequency.Weekly, Weekdays: [DayOfWeek.Friday], WeekStart: 0);
		await Service.PreviewAsync(id, RecurringSuccessorKind.Replace, new(2026, 12, 1), rule, ct: ct);
		await Service.ApplyAsync(id, request, "token", RecurringSuccessorKind.Replace, new(2026, 12, 1), rule, ct: ct);
		await _successors.Received(1).ApplyAsync(id, request, Arg.Is<RecurringSuccessorRequest>(r => r != null && r.Rule!.WeekStart == DayOfWeek.Sunday), "token", null, ct);
		_calendar.ReceivedCalls().Should().BeEmpty("preview/apply alignment cannot be changed by preferences or a retry");
	}
}

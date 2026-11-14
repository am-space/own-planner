using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Mcp.Tools.Tests;

public sealed class RecurringTaskToolsTests
{
	private readonly IRecurringTaskManagement _management = Substitute.For<IRecurringTaskManagement>();
	private readonly IPlanningWorkReader _reader = Substitute.For<IPlanningWorkReader>();
	private readonly ITaskOccurrenceHistory _history = Substitute.For<ITaskOccurrenceHistory>();
	private readonly IRecurringTaskLifecycle _lifecycle = Substitute.For<IRecurringTaskLifecycle>();
	private RecurringTaskTools Tools => new(_management, _reader, _history, _lifecycle);

	[Fact]
	public void SchemasHaveExactNamesRequiredIntentAndNoTenantSelectors()
	{
		var required = new Dictionary<string, string[]>
		{
			["recurring_task_create"] = ["title", "taskListId", "startDate", "frequency", "missedPolicy"],
			["recurring_task_configure"] = ["taskId", "startDate", "frequency", "missedPolicy"],
			["recurring_task_get"] = ["taskId"], ["planning_work_query"] = ["startDate", "endExclusiveDate"],
			["task_occurrence_history"] = ["basis"], ["task_occurrence_get"] = ["taskId", "occurrenceDate"],
			["task_occurrence_complete"] = ["taskId", "occurrenceDate"], ["task_occurrence_cancel"] = ["taskId", "occurrenceDate"],
			["recurring_task_successor_preview"] = ["taskId", "kind", "startDate"],
			["recurring_task_successor_apply"] = ["taskId", "requestId", "previewToken", "kind", "startDate"],
			["recurring_task_end"] = ["taskId"]
		};
		var methods = typeof(RecurringTaskTools).GetMethods().Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null).ToArray();
		methods.Should().HaveCount(required.Count);
		foreach (var method in methods)
		{
			var tool = McpServerTool.Create(method, Tools).ProtocolTool;
			tool.InputSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Should().BeEquivalentTo(required[tool.Name]);
			tool.InputSchema.GetProperty("properties").EnumerateObject().Select(x => x.Name).Should().NotContain(["userId", "databasePath", "ct"]);
			var attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;
			(tool.Annotations?.ReadOnlyHint ?? false).Should().Be(attribute.ReadOnly);
		}
	}

	[Theory]
	[InlineData("2026-11-14T00:00:00Z", "Weekly", "RetainEachObligation")]
	[InlineData("2026-11-14", "Daily", "RetainEachObligation")]
	[InlineData("2026-11-14", "Weekly", "")]
	[InlineData("2026-11-14", "Weekly", "0")]
	public async Task InvalidCreationIntentCannotReachApplication(string start, string frequency, string policy)
	{
		var result = await Tools.Create("Task", Guid.NewGuid(), start, frequency, policy, weekdays: ["Friday"], ct: TestContext.Current.CancellationToken);
		Error(result).Should().NotBeNullOrWhiteSpace();
		_management.ReceivedCalls().Should().BeEmpty();
	}

	[Theory]
	[InlineData("2026-11-13")]
	[InlineData("2026-11-13T12:00:00")]
	[InlineData("2026-11-13 12:00:00Z")]
	[InlineData("not a time")]
	public async Task AmbiguousCompletionTimeCannotWrite(string instant)
	{
		Error(await Tools.Complete(Guid.NewGuid(), "2026-11-13", instant, TestContext.Current.CancellationToken)).Should().Contain("explicit");
		_lifecycle.ReceivedCalls().Should().BeEmpty();
	}

	[Fact]
	public async Task ExplicitOffsetBecomesActualUtcInstantWithoutChangingScheduledDate()
	{
		var task = Guid.NewGuid(); var ct = TestContext.Current.CancellationToken;
		await Tools.Complete(task, "2026-11-06", "2026-11-09T09:00:00-05:00", ct);
		await _lifecycle.Received(1).CompleteOccurrenceAsync(new(task, new(2026, 11, 6)), new DateTime(2026, 11, 9, 14, 0, 0, DateTimeKind.Utc), ct);
	}

	[Fact]
	public async Task QueryAndHistoryPreserveBasisFiltersAndPageBounds()
	{
		var ct = TestContext.Current.CancellationToken; var task = Guid.NewGuid(); var list = Guid.NewGuid(); var context = Guid.NewGuid();
		await Tools.Query("2026-11-01", "2026-12-01", true, "All", "Occurrence", "None", task, list, contextId: context, offset: 12, limit: 3, ct: ct);
		await _reader.Received(1).QueryAsync(Arg.Is<PlanningWorkQuery>(q => q != null && q.Range.StartDate == new DateOnly(2026, 11, 1)
			&& q.TaskId == task && q.TaskListId == list && q.ContextId == context && q.Status == null && q.Kind == PlanningWorkKind.Occurrence
			&& q.OrdinaryTasks == OrdinaryTaskSelection.None && q.IncludeRetainedBeforeStart && q.Offset == 12 && q.Limit == 3), ct);
		await Tools.History("CompletionTime", startUtc: "2026-11-01T00:00:00Z", endExclusiveUtc: "2026-12-01T00:00:00Z", taskId: task, ct: ct);
		await _history.Received(1).QueryAsync(Arg.Is<OccurrenceHistoryQuery>(q => q != null && q.Basis == OccurrenceHistoryBasis.CompletionTime
			&& q.ScheduledRange == null && q.StartUtc!.Value.Kind == DateTimeKind.Utc && q.TaskId == task), ct);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("KeepStartAsSeparateWork")]
	[InlineData("MoveStartAfterFacts")]
	public async Task SuccessorForwardsOnlyExplicitResolutionAndRetryIdentity(string? resolution)
	{
		var task = Guid.NewGuid(); var request = Guid.NewGuid(); var ct = TestContext.Current.CancellationToken;
		await Tools.Apply(task, request, "preview", "Resume", "2026-12-01", resolution: resolution, ct: ct);
		await _management.Received(1).ApplyAsync(task, request, "preview", RecurringSuccessorKind.Resume, new(2026, 12, 1),
			null, null, null, false, resolution is null ? null : Enum.Parse<SuccessorConflictResolution>(resolution), ct);
	}

	[Fact]
	public async Task InvalidResolutionAndPartialRuleDoNotDispatch()
	{
		Error(await Tools.Apply(Guid.NewGuid(), Guid.NewGuid(), "preview", "Replace", "2026-12-01", resolution: "automatic", ct: TestContext.Current.CancellationToken)).Should().Contain("Expected");
		Error(await Tools.Preview(Guid.NewGuid(), "Replace", "2026-12-01", interval: 2, ct: TestContext.Current.CancellationToken)).Should().Contain("complete day selection");
		_management.ReceivedCalls().Should().BeEmpty();
	}

	[Fact]
	public async Task ApplicationErrorsBecomeSafeToolErrorsAndCancellationPropagates()
	{
		var id = Guid.NewGuid();
		_management.GetAsync(id, Arg.Any<CancellationToken>()).Returns<Task<RecurringTaskDetails>>(_ => throw new KeyNotFoundException("Available task not found."));
		Error(await Tools.Get(id, TestContext.Current.CancellationToken)).Should().Be("Available task not found.");
		_management.GetAsync(id, Arg.Any<CancellationToken>()).Returns<Task<RecurringTaskDetails>>(_ => throw new OperationCanceledException());
		await ((Func<Task>)(() => Tools.Get(id, TestContext.Current.CancellationToken))).Should().ThrowAsync<OperationCanceledException>();
	}

	private static string? Error(object result) => JsonSerializer.SerializeToElement(result).GetProperty("error").GetString();
}

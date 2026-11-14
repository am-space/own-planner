using System.Text.Json;
using FluentAssertions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Application.Tests.Chat;

public partial class TaskPlanningMcpAdapterTests
{
	[Theory]
	[InlineData("recurring_task_get")]
	[InlineData("recurring_task_configure")]
	[InlineData("task_occurrence_get")]
	[InlineData("task_occurrence_complete")]
	[InlineData("task_occurrence_cancel")]
	[InlineData("recurring_task_end")]
	[InlineData("recurring_task_successor_preview")]
	[InlineData("recurring_task_successor_apply")]
	[InlineData("task_occurrence_history")]
	public async Task RecurrenceTargetsCannotEscapeDelegatedList(string tool)
	{
		var ct = TestContext.Current.CancellationToken; var list = Guid.NewGuid(); var task = Guid.NewGuid();
		var inner = new FakeMcpAdapter(tool, "taskitem_get", "tasklist_get");
		inner.Results["taskitem_get"] = JsonSerializer.Serialize(new { id = task, taskListId = Guid.NewGuid() });
		var adapter = await TaskPlanningMcpAdapter.CreateAsync(inner, null, list, ct);
		var action = () => adapter.CallToolAsync(tool, new Dictionary<string, object?> { ["taskId"] = task }, ct);
		await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*outside the delegated scope*");
		inner.Calls.Should().NotContain(c => c.ToolName == tool);
		adapter.Actions.Should().BeEmpty();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task RecurrenceRangeReadsForceDelegatedFilters(bool listScope)
	{
		var ct = TestContext.Current.CancellationToken; var list = Guid.NewGuid(); var context = Guid.NewGuid();
		var inner = new FakeMcpAdapter("planning_work_query", "tasklist_get", "context_get");
		inner.Results["tasklist_get"] = JsonSerializer.Serialize(new { id = list, contextId = context });
		var adapter = await TaskPlanningMcpAdapter.CreateAsync(inner, context, listScope ? list : null, ct);
		await adapter.CallToolAsync("planning_work_query", new Dictionary<string, object?> { ["contextId"] = Guid.NewGuid(), ["taskListId"] = list }, ct);
		var args = inner.Calls.Last().Arguments!;
		args["contextId"].Should().Be(context); args["taskListId"].Should().Be(list);
	}

	[Theory]
	[InlineData("recurring_task_create")]
	[InlineData("planning_work_query")]
	[InlineData("task_occurrence_complete")]
	public async Task RecurrenceRejectsTargetsOutsideDelegatedContext(string tool)
	{
		var ct = TestContext.Current.CancellationToken; var list = Guid.NewGuid(); var context = Guid.NewGuid();
		var inner = new FakeMcpAdapter(tool, "tasklist_get", "context_get", "taskitem_get");
		inner.Results["tasklist_get"] = JsonSerializer.Serialize(new { id = list, contextId = Guid.NewGuid() });
		inner.Results["taskitem_get"] = JsonSerializer.Serialize(new { id = Guid.NewGuid(), taskListId = list });
		var adapter = await TaskPlanningMcpAdapter.CreateAsync(inner, context, null, ct);
		var action = () => adapter.CallToolAsync(tool, new Dictionary<string, object?> { ["taskListId"] = list, ["taskId"] = Guid.NewGuid() }, ct);
		await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*outside the delegated context*");
		inner.Calls.Should().NotContain(c => c.ToolName == tool);
	}

	[Fact]
	public async Task ScopedHistoryRequiresExplicitTaskAndCreationRequiresScopedList()
	{
		var ct = TestContext.Current.CancellationToken; var list = Guid.NewGuid();
		var inner = new FakeMcpAdapter("recurring_task_create", "task_occurrence_history", "tasklist_get");
		var adapter = await TaskPlanningMcpAdapter.CreateAsync(inner, null, list, ct);
		await ((Func<Task>)(() => adapter.CallToolAsync("task_occurrence_history", cancellationToken: ct))).Should().ThrowAsync<InvalidOperationException>();
		await ((Func<Task>)(() => adapter.CallToolAsync("recurring_task_create", new Dictionary<string, object?> { ["taskListId"] = Guid.NewGuid() }, ct))).Should().ThrowAsync<InvalidOperationException>();
		inner.Calls.Should().OnlyContain(c => c.ToolName == "tasklist_get");
	}

	[Fact]
	public void RecurrenceWritesRemainUnavailableInReflectionAndSystemAnalysis()
	{
		string[] writes = ["recurring_task_create", "recurring_task_configure", "task_occurrence_complete", "task_occurrence_cancel", "recurring_task_end", "recurring_task_successor_apply"];
		foreach (var mode in new[] { PlanningMode.Reflection, PlanningMode.SystemAnalysis })
		{
			var config = ModeConfig.All[mode];
			config.AllowedTools.Should().NotContain(writes);
			config.AllowedTools.Should().Contain(["planning_work_query", "task_occurrence_history", "task_occurrence_get", "recurring_task_get"]);
		}
		ChatSkillRegistry.ReadTools.Should().NotContain(writes);
	}
}

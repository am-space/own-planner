using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OwnPlanner.Application.Chat;
using OwnPlanner.Domain;

namespace OwnPlanner.Infrastructure.Tests.Adapters;

public sealed partial class ChatSkillOrchestrationTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task RequestedCaptureTaskRoutesDirectlyToInboxOrUserSelectedListWithoutMovingSource(bool selectedList)
	{
		var ct = TestContext.Current.CancellationToken;
		var source = Guid.NewGuid(); var list = selectedList ? Guid.NewGuid() : WellKnownIds.InboxTaskList;
		using var provider = new ScriptedProvider(Calls(Call("noteitem_get", new { id = source }), Call("tasklist_all", new { includeUnassigned = true })),
			Calls(Call("taskitem_create", new { title = "Book the appointment", description = "Call the clinic", taskListId = list })),
			Final("Created the task; the source note is unchanged."));
		var mcp = new RecordingMcpAdapter();
		mcp.Results["noteitem_get"] = JsonSerializer.Serialize(new { id = source, content = "Call the clinic", noteListId = WellKnownIds.InboxNoteList });
		mcp.Results["tasklist_all"] = JsonSerializer.Serialize(new[] { new { id = list, title = selectedList ? "Health" : "Inbox", isSystem = !selectedList } });
		mcp.Results["taskitem_create"] = JsonSerializer.Serialize(new { id = Guid.NewGuid(), taskListId = list });
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.Reflection);
		await using var planning = new PlanningService(adapter, mcp, NullLogger<PlanningService>.Instance);
		await planning.SwitchModeAsync(PlanningMode.Reflection, ct);
		await planning.GetResponseAsync(selectedList ? "Create a task from this capture in Health." : "Create a task from this capture.", ct);
		mcp.Calls.Should().Equal("reflection_report_get", "calendar_period_get", "noteitem_get", "tasklist_all", "taskitem_create");
		Names(provider.Requests[0]).Should().Contain("taskitem_create", "noteitem_assign").And.NotContain("skill_load", "taskitem_update");
		mcp.Arguments.Last().Values!["taskListId"]!.ToString().Should().Be(list.ToString());
		mcp.Arguments.Single(a => a.Tool == "tasklist_all").Values!["includeUnassigned"]!.ToString().Should().Be("True");
		provider.Requests[1].ToString().Should().Contain("Call the clinic");
		provider.Requests[2].ToString().Should().Contain(list.ToString());
		var prompt = ContentsText(provider.Requests[0]);
		prompt.Should().Contain("Creating a task preserves the source note").And.Contain("without redundant confirmation")
			.And.Contain("Unless the user chooses another task list").And.Contain("clarify ambiguous destinations")
			.And.Contain("Do not automatically retry task creation after an uncertain result");
		mcp.Arguments[0].Values!["calendarPeriod"].Should().Be("lastWeek");
	}

	[Fact]
	public async Task RequestedNoteMovementRoutesDirectlyWithoutCreatingTask()
	{
		var source = Guid.NewGuid(); var destination = Guid.NewGuid();
		using var provider = new ScriptedProvider(Calls(Call("notelist_all", new { includeUnassigned = true })),
			Calls(Call("noteitem_assign", new { noteId = source, noteListId = destination })), Final("Moved the note."));
		var mcp = new RecordingMcpAdapter();
		mcp.Results["notelist_all"] = JsonSerializer.Serialize(new[] { new { id = destination, title = "Reference", isSystem = false } });
		mcp.Results["noteitem_assign"] = JsonSerializer.Serialize(new { success = true, noteId = source, noteListId = destination });
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.Reflection);
		await adapter.GetResponse("Move this capture to my Reference list.", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal("notelist_all", "noteitem_assign");
		Names(provider.Requests[0]).Should().Contain("noteitem_assign").And.NotContain("skill_load");
		mcp.Arguments.Last().Values!["noteId"]!.ToString().Should().Be(source.ToString());
		mcp.Arguments.Last().Values!["noteListId"]!.ToString().Should().Be(destination.ToString());
		provider.Requests[2].ToString().Should().Contain("success").And.Contain(destination.ToString());
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ExplorationAndAmbiguousMovementCanRespondWithoutWrites(bool ambiguousMove)
	{
		using var provider = new ScriptedProvider(Final(ambiguousMove ? "Which Reference list do you mean?" : "Would making a task help?"));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.Reflection);
		await adapter.GetResponse(ambiguousMove ? "Move it to the other list." : "I'm considering acting on this capture.", TestContext.Current.CancellationToken);
		mcp.Calls.Should().BeEmpty();
		ContentsText(provider.Requests[0]).Should().Contain("Discuss exploratory ideas without writes")
			.And.Contain("only for an explicitly requested move with an unambiguous destination");
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ExplicitBothActionsCarryIndependentSuccessAndPartialFailureToProvider(bool createFails)
	{
		using var provider = new ScriptedProvider(Calls(Call("taskitem_create", new { title = "Next step", taskListId = WellKnownIds.InboxTaskList })),
			Calls(Call("noteitem_assign", new { noteId = Guid.NewGuid(), noteListId = Guid.NewGuid() })), Final("One action succeeded; the other failed."));
		var mcp = new RecordingMcpAdapter();
		mcp.Results["taskitem_create"] = createFails ? "{\"error\":\"TaskList not found\"}" : "{\"id\":\"created-task\"}";
		mcp.Results["noteitem_assign"] = createFails ? "{\"success\":true}" : "{\"error\":\"NoteList not found\"}";
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.Reflection);
		await adapter.GetResponse("Create an Inbox task and move the source note to my Reference list.", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal("taskitem_create", "noteitem_assign");
		provider.Requests[1].ToString().Should().Contain(createFails ? "TaskList not found" : "created-task");
		provider.Requests[2].ToString().Should().Contain(createFails ? "success" : "NoteList not found");
		ContentsText(provider.Requests[0]).Should().Contain("execute and report each separately from its actual tool result")
			.And.Contain("not an atomic conversion").And.Contain("never claim the capture is fully processed");
	}

	[Fact]
	public async Task UncertainCreationResultReachesProviderWithoutAnOrchestratorRetryOrNoteMovement()
	{
		using var provider = new ScriptedProvider(Calls(Call("taskitem_create", new { title = "Next step", taskListId = WellKnownIds.InboxTaskList })),
			Final("Task creation is uncertain; I have not moved the source note."));
		var mcp = new RecordingMcpAdapter();
		mcp.Results["taskitem_create"] = "{\"error\":\"Connection lost; outcome unknown\"}";
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.Reflection);
		await adapter.GetResponse("Create a task from this capture.", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal("taskitem_create");
		provider.Requests[1].ToString().Should().Contain("outcome unknown");
		ContentsText(provider.Requests[0]).Should().Contain("Do not automatically retry task creation after an uncertain result");
	}

	[Theory]
	[InlineData("taskitem_update")]
	[InlineData("taskitem_set_focus_date")]
	[InlineData("taskitem_complete")]
	[InlineData("taskitem_assign")]
	[InlineData("taskitem_delete")]
	[InlineData("taskitem_restore")]
	[InlineData("noteitem_delete")]
	[InlineData("skill_load")]
	public async Task ReflectionRejectsBroaderWritesEvenWhenProviderCallsThem(string tool)
	{
		using var provider = new ScriptedProvider(Calls(Call(tool, new { })), Final("Unavailable"));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.Reflection);
		await adapter.GetResponse("Attempt a broader operation", TestContext.Current.CancellationToken);
		mcp.Calls.Should().BeEmpty();
		provider.Requests[1].ToString().Should().Contain("not permitted for this model request");
	}
}

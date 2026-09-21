using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Common;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Mcp.Tools.Tests;

public class TaskItemToolsTests
{
	private const int PreviewMaxLength = 200;

	private readonly ITaskItemService _service = Substitute.For<ITaskItemService>();
	private readonly TaskItemTools _tools;

	public TaskItemToolsTests()
	{
		_tools = new TaskItemTools(_service);
	}

	private static TaskItemDto Task(string? description = null) => new(
		Id: Guid.NewGuid(),
		Title: "Title",
		Description: description,
		IsCompleted: false,
		IsImportant: false,
		CreatedAt: DateTime.UtcNow,
		UpdatedAt: DateTime.UtcNow,
		DueAt: null,
		CompletedAt: null,
		TaskListId: Guid.NewGuid(),
		FocusAt: null,
		GoalId: null);

	// Serialize the tool's anonymous envelope the same way the runtime does (camelCase Web defaults),
	// so we assert the actual on-the-wire shape.
	private static JsonElement AsJson(object result) =>
		JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

	[Fact]
	public void UpdateTask_McpSdkSchema_ExposesOptionalClearDueAt()
	{
		var tool = ModelContextProtocol.Server.McpServerTool.Create(
			typeof(TaskItemTools).GetMethod(nameof(TaskItemTools.UpdateTask))!, _tools,
			new() { SerializerOptions = TaskToolSerialization.Options });
		tool.ProtocolTool.Name.Should().Be("taskitem_update");
		var schema = tool.ProtocolTool.InputSchema;
		schema.GetProperty("properties").GetProperty("clearDueAt").GetProperty("type").GetString().Should().Be("boolean");
		schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Should().Equal("id");
		tool.ProtocolTool.Description.Should().Contain("clearDueAt=true");
	}

	[Fact]
	public void SdkSerialization_RetainsNullDeadlineOnlyForFullTaskResults()
	{
		var dto = Task();
		var full = JsonSerializer.SerializeToElement(dto, TaskToolSerialization.Options);
		full.GetProperty("dueAt").ValueKind.Should().Be(JsonValueKind.Null);
		full.TryGetProperty("description", out _).Should().BeFalse();
		var list = new TaskItemListDto(dto.Id, dto.Title, null, false, false, null, null, dto.TaskListId, null, null);
		JsonSerializer.SerializeToElement(list, TaskToolSerialization.Options).TryGetProperty("dueAt", out _).Should().BeFalse();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("2026-09-20")]
	[InlineData("not a date")]
	public async Task UpdateTask_ClearDueAt_SkipsParsingAndReturnsNullDeadline(string? dueAt)
	{
		var dto = Task();
		_service.UpdateAsync(dto.Id, ct: Arg.Any<CancellationToken>(), clearDueAt: true).Returns(dto);

		var json = AsJson(await _tools.UpdateTask(dto.Id, dueAt: dueAt, clearDueAt: true));

		json.GetProperty("dueAt").ValueKind.Should().Be(JsonValueKind.Null);
		json.GetProperty("id").GetGuid().Should().Be(dto.Id);
		await _service.Received(1).UpdateAsync(dto.Id, ct: Arg.Any<CancellationToken>(), clearDueAt: true);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("2026-09-20")]
	public async Task UpdateTask_WithoutClearing_PreservesOrdinaryDateParsing(string? dueAt)
	{
		var id = Guid.NewGuid();
		await _tools.UpdateTask(id, dueAt: dueAt);
		await _service.Received(1).UpdateAsync(id, dueAt: string.IsNullOrEmpty(dueAt) ? null : new DateTime(2026, 9, 20), ct: Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UpdateTask_InvalidDate_ReturnsErrorBeforeAnyMutation()
	{
		var json = AsJson(await _tools.UpdateTask(Guid.NewGuid(), title: "Must not change", dueAt: "invalid"));
		json.GetProperty("error").GetString().Should().Be("Invalid date format for dueAt");
		_service.ReceivedCalls().Should().BeEmpty();
	}

	[Fact]
	public async Task UpdateTask_ClearMissingTask_ReturnsApplicationError()
	{
		var id = Guid.NewGuid();
		_service.UpdateAsync(id, ct: Arg.Any<CancellationToken>(), clearDueAt: true)
			.Returns(System.Threading.Tasks.Task.FromException<TaskItemDto>(new KeyNotFoundException("Task not found")));
		var json = AsJson(await _tools.UpdateTask(id, clearDueAt: true));
		json.GetProperty("error").GetString().Should().Be("Task not found");
	}

	[Fact]
	public async Task ListTasks_ReturnsPagingEnvelope()
	{
		var page = new PagedResult<TaskItemDto>([Task(), Task()], TotalCount: 10, Offset: 0, Limit: 25);
		_service.ListPagedAsync(false, false, 0, 25, Arg.Any<CancellationToken>()).Returns(page);

		var json = AsJson(await _tools.ListTasks());

		json.GetProperty("totalCount").GetInt32().Should().Be(10);
		json.GetProperty("offset").GetInt32().Should().Be(0);
		json.GetProperty("limit").GetInt32().Should().Be(25);
		json.GetProperty("hasMore").GetBoolean().Should().BeTrue();
		json.GetProperty("items").GetArrayLength().Should().Be(2);
	}

	[Fact]
	public async Task ListTasks_Items_AreSlim_NoAuditTimestamps()
	{
		var page = new PagedResult<TaskItemDto>([Task("short")], TotalCount: 1, Offset: 0, Limit: 25);
		_service.ListPagedAsync(false, false, 0, 25, Arg.Any<CancellationToken>()).Returns(page);

		var json = AsJson(await _tools.ListTasks());
		var item = json.GetProperty("items")[0];

		item.TryGetProperty("createdAt", out _).Should().BeFalse();
		item.TryGetProperty("updatedAt", out _).Should().BeFalse();
		item.GetProperty("title").GetString().Should().Be("Title");
	}

	[Fact]
	public async Task ListTasks_LongDescription_IsTruncatedWithHint()
	{
		var longDescription = new string('x', 500);
		var page = new PagedResult<TaskItemDto>([Task(longDescription)], TotalCount: 1, Offset: 0, Limit: 25);
		_service.ListPagedAsync(false, false, 0, 25, Arg.Any<CancellationToken>()).Returns(page);

		var json = AsJson(await _tools.ListTasks());
		var description = json.GetProperty("items")[0].GetProperty("description").GetString()!;

		description.Should().StartWith(new string('x', PreviewMaxLength));
		description.Should().Contain("truncated");
		description.Should().Contain("taskitem_get");
		description.Length.Should().BeLessThan(longDescription.Length);
	}

	[Fact]
	public async Task ListTasks_WithTaskListId_DelegatesToByListPaging()
	{
		var listId = Guid.NewGuid();
		var page = new PagedResult<TaskItemDto>([], TotalCount: 0, Offset: 0, Limit: 25);
		_service.ListByTaskListPagedAsync(listId, false, false, 0, 25, Arg.Any<CancellationToken>()).Returns(page);

		await _tools.ListTasks(taskListId: listId);

		await _service.Received(1).ListByTaskListPagedAsync(listId, false, false, 0, 25, Arg.Any<CancellationToken>());
		await _service.DidNotReceive().ListPagedAsync(
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task DeleteTask_MovesTaskToTrashThroughCompatibleTool()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();

		var json = AsJson(await _tools.DeleteTask(id, ct));

		await _service.Received(1).DeleteAsync(id, ct);
		json.GetProperty("success").GetBoolean().Should().BeTrue();
		json.GetProperty("id").GetGuid().Should().Be(id);
	}

	[Fact]
	public async Task CompleteTask_ForwardsCancellation()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();

		await _tools.CompleteTask(id, ct);

		await _service.Received(1).CompleteAsync(id, ct);
	}

	[Fact]
	public async Task ListTrash_ReturnsPagingEnvelopeWithTrashedAt()
	{
		var ct = TestContext.Current.CancellationToken;
		var item = new TrashedTaskItemDto(
			Guid.NewGuid(), "trashed", new string('x', 250), false, false, null, null, Guid.NewGuid(), null, null, DateTime.UtcNow);
		_service.ListTrashedPagedAsync(0, 25, ct)
			.Returns(new PagedResult<TrashedTaskItemDto>([item], 1, 0, 25));

		var json = AsJson(await _tools.ListTrash(cancellationToken: ct));

		json.GetProperty("items")[0].GetProperty("trashedAt").GetDateTime().Should().Be(item.TrashedAt);
		json.GetProperty("items")[0].GetProperty("description").GetString().Should().EndWith("[truncated — call taskitem_get for full description]");
		json.GetProperty("totalCount").GetInt32().Should().Be(1);
		await _service.Received(1).ListTrashedPagedAsync(0, 25, ct);
	}

	[Fact]
	public async Task RestoreTask_ReturnsApplicationFailureAsToolError()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		_service.RestoreAsync(id, ct)
			.Returns(System.Threading.Tasks.Task.FromException(new InvalidOperationException("Original list missing")));

		var json = AsJson(await _tools.RestoreTask(id, ct));

		json.GetProperty("error").GetString().Should().Contain("Original list missing");
		await _service.Received(1).RestoreAsync(id, ct);
	}
}

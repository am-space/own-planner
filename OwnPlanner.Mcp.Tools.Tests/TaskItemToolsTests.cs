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
		var id = Guid.NewGuid();

		var json = AsJson(await _tools.DeleteTask(id));

		await _service.Received(1).DeleteAsync(id, Arg.Any<CancellationToken>());
		json.GetProperty("success").GetBoolean().Should().BeTrue();
		json.GetProperty("id").GetGuid().Should().Be(id);
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

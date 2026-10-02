using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Mcp.Tools.Tests;

public sealed class TaskGoalLinkToolsTests
{
	private readonly ITaskGoalLinkService _service = Substitute.For<ITaskGoalLinkService>();
	private TaskGoalLinkTools Tools => new(_service);

	[Fact]
	public void SharedSdkSchemaExposesOnlyTaskAndGoalIdsAndRequiresBoth()
	{
		var tool = McpServerTool.Create(typeof(TaskGoalLinkTools).GetMethod(nameof(TaskGoalLinkTools.LinkGoal))!, Tools).ProtocolTool;
		tool.Name.Should().Be("taskitem_link_goal");
		tool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("taskId", "goalId");
		tool.InputSchema.GetProperty("required").EnumerateArray().Select(p => p.GetString()).Should().BeEquivalentTo("taskId", "goalId");
		tool.Description.Should().Contain("user confirms").And.Contain("only goalId");
		(tool.Annotations?.ReadOnlyHint).Should().NotBe(true);
	}

	[Fact]
	public async Task ReturnsTaskAndPropagatesCancellation()
	{
		var id = Guid.NewGuid(); var goal = Guid.NewGuid(); var ct = TestContext.Current.CancellationToken;
		var dto = new TaskItemDto(id, "Physiotherapy", null, false, false, DateTime.UtcNow, DateTime.UtcNow, null, null, Guid.NewGuid(), null, goal);
		_service.LinkAsync(id, goal, ct).Returns(dto);
		(await Tools.LinkGoal(id, goal, ct)).Should().BeSameAs(dto);
		await _service.Received(1).LinkAsync(id, goal, ct);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ApplicationFailuresReturnCompatibleErrorShape(bool missing)
	{
		var id = Guid.NewGuid(); var goal = Guid.NewGuid();
		_service.LinkAsync(id, goal, Arg.Any<CancellationToken>()).Returns<TaskItemDto>(_ => throw (missing ? new KeyNotFoundException("Goal not found") : new InvalidOperationException("Only active goals can be linked")));
		var result = JsonSerializer.SerializeToElement(await Tools.LinkGoal(id, goal, TestContext.Current.CancellationToken));
		result.EnumerateObject().Select(p => p.Name).Should().Equal("error");
		result.GetProperty("error").GetString().Should().Be(missing ? "Goal not found" : "Only active goals can be linked");
	}
}

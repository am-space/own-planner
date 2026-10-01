using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using OwnPlanner.Application.Goals;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Mcp.Tools.Tests;

public sealed class GoalToolsTests
{
	private readonly IGoalService _service = Substitute.For<IGoalService>();
	private GoalTools Tools => new(_service);

	[Theory]
	[InlineData("Paused", GoalStatus.Paused)]
	[InlineData("active", GoalStatus.Active)]
	public async Task PauseAndResumeUseExistingUpdateContractAndReturnGuidance(string input, GoalStatus status)
	{
		var id = Guid.NewGuid();
		var dto = new GoalDto(id, "Spanish", null, GoalHorizon.Yearly, "2026", null, status, null, null, DateTime.UtcNow, DateTime.UtcNow,
			ActiveGoalWarning: status == GoalStatus.Active ? "6 active goals. Pause or drop one?" : null);
		_service.UpdateAsync(id, status: status, ct: Arg.Any<CancellationToken>()).Returns(dto);
		(await Tools.UpdateGoal(id, status: input)).Should().BeSameAs(dto);
		await _service.Received(1).UpdateAsync(id, status: status, ct: Arg.Any<CancellationToken>());
	}

	[Theory]
	[InlineData("unknown")]
	[InlineData("999")]
	public async Task InvalidStatusDoesNotCallApplication(string status)
	{
		var result = System.Text.Json.JsonSerializer.Serialize(await Tools.UpdateGoal(Guid.NewGuid(), status: status));
		result.Should().Contain("Invalid status").And.Contain("Paused");
		_service.ReceivedCalls().Should().BeEmpty();
	}

	[Fact]
	public async Task MissingGoalAndInvalidTransitionRemainApplicationErrors()
	{
		var id = Guid.NewGuid();
		_service.UpdateAsync(id, status: GoalStatus.Paused, ct: Arg.Any<CancellationToken>()).Returns<GoalDto>(_ => throw new ArgumentException("Only active goals can be paused."));
		System.Text.Json.JsonSerializer.Serialize(await Tools.UpdateGoal(id, status: "Paused")).Should().Contain("Only active goals can be paused");
		_service.GetAsync(id, Arg.Any<CancellationToken>()).Returns((GoalDto?)null);
		System.Text.Json.JsonSerializer.Serialize(await Tools.GetGoal(id)).Should().Contain("Goal not found");
	}

	[Fact]
	public void SharedSdkSchemasRetainNamesAndRequiredInputsAndDescribeAdvisoryBehavior()
	{
		var update = McpServerTool.Create(typeof(GoalTools).GetMethod(nameof(GoalTools.UpdateGoal))!, Tools).ProtocolTool;
		update.Name.Should().Be("goal_update");
		update.InputSchema.GetProperty("required").EnumerateArray().Select(v => v.GetString()).Should().Equal("id");
		update.InputSchema.GetProperty("properties").GetProperty("status").GetProperty("type").EnumerateArray().Select(v => v.GetString()).Should().Contain("string");
		update.Description.Should().Contain("Paused").And.Contain("advisory");
		var create = McpServerTool.Create(typeof(GoalTools).GetMethod(nameof(GoalTools.CreateGoal))!, Tools).ProtocolTool;
		create.Name.Should().Be("goal_create");
		create.InputSchema.GetProperty("required").EnumerateArray().Select(v => v.GetString()).Should().Equal("title", "horizon");
		create.Description.Should().Contain("activeGoalWarning");
		var list = McpServerTool.Create(typeof(GoalTools).GetMethod(nameof(GoalTools.ListGoals))!, Tools).ProtocolTool;
		list.Annotations!.ReadOnlyHint.Should().BeTrue();
		list.Description.Should().Contain("Paused");
	}
}

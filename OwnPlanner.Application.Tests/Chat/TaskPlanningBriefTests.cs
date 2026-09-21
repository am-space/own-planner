using System.Text.Json;
using FluentAssertions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Application.Tests.Chat;

public sealed class TaskPlanningBriefTests
{
	[Fact]
	public void LegacyRequest_DefaultsToExecutionAndOptionalScopesRemainCompatible()
	{
		var request = TaskPlanningRequestParser.Parse(new Dictionary<string, object?> { ["objective"] = "Plan", ["contextId"] = "", ["taskListId"] = null });
		request.Should().Be(new TaskPlanningAgentRequest("Plan"));
	}

	[Fact]
	public void StructuredBrief_PreservesSelectedContextAndDecisions()
	{
		var id = Guid.NewGuid();
		var request = TaskPlanningRequestParser.Parse(new Dictionary<string, object?>
		{
			["objective"] = "Explore a launch plan", ["behavior"] = "proposal", ["taskListId"] = id,
			["brief"] = new { constraints = new[] { "Two hours per week" }, userDecisions = new[] { "No weekend work" }, entityReferences = new[] { new { kind = "taskList", id, label = "Launch" } } }
		});
		request.Behavior.Should().Be(TaskPlanningBehavior.Proposal);
		request.TaskListId.Should().Be(id);
		request.Brief!.Constraints.Should().Equal("Two hours per week");
		request.Brief.UserDecisions.Should().Equal("No weekend work");
		request.Brief.EntityReferences.Should().ContainSingle().Which.Id.Should().Be(id);
	}

	[Theory]
	[InlineData("behavior", "\"unknown\"")]
	[InlineData("behavior", "null")]
	[InlineData("behavior", "true")]
	[InlineData("objective", "\"\"")]
	[InlineData("objective", "42")]
	[InlineData("contextId", "\"not-a-uuid\"")]
	[InlineData("brief", "{\"history\":\"private conversation\"}")]
	[InlineData("brief", "{\"constraints\":[null]}")]
	[InlineData("brief", "{\"constraints\":[42]}")]
	[InlineData("brief", "{\"entityReferences\":[null]}")]
	[InlineData("brief", "{\"entityReferences\":[{\"kind\":\"user\",\"id\":\"11111111-1111-1111-1111-111111111111\"}]}")]
	[InlineData("brief", "{\"entityReferences\":[{\"kind\":\"task\"}]}")]
	public void InvalidShape_FailsWithBoundedMessageWithoutEchoingContext(string name, string json)
	{
		var act = () => TaskPlanningRequestParser.Parse(new Dictionary<string, object?> { ["objective"] = "Plan", [name] = JsonSerializer.Deserialize<JsonElement>(json) });
		act.Should().Throw<InvalidOperationException>().Which.Message.Should().StartWith("Invalid task-planning request.").And.NotContain("private conversation");
	}

	[Fact]
	public void OversizedBrief_RejectsRatherThanDroppingConstraints()
	{
		var briefs = new TaskPlanningBrief[]
		{
			new(Constraints: Enumerable.Repeat("constraint", 9).ToArray()),
			new(Constraints: [new string('x', 301)]),
			new(UserDecisions: Enumerable.Repeat("decision", 9).ToArray()),
			new(UserDecisions: [new string('x', 301)]),
			new(EntityReferences: Enumerable.Repeat(new TaskPlanningEntityReference("task", Guid.NewGuid()), 17).ToArray()),
			new(EntityReferences: [new("task", Guid.NewGuid(), new string('x', 121))])
		};
		foreach (var brief in briefs)
		{
			var act = () => TaskPlanningRequestParser.Validate(new("Plan", Brief: brief));
			act.Should().Throw<InvalidOperationException>();
		}
		var objective = () => TaskPlanningRequestParser.Validate(new(new string('x', 2001)));
		objective.Should().Throw<InvalidOperationException>();
		TaskPlanningRequestParser.Validate(new(new string('x', 2000), Brief: new(Constraints: Enumerable.Repeat(new string('x', 300), 8).ToArray())));
	}
}

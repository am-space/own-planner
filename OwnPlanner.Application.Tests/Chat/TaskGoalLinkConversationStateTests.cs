using FluentAssertions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Application.Tests.Chat;

public sealed class TaskGoalLinkConversationStateTests
{
	[Fact]
	public void OffersAndDeclinesAreRetainedIndependentlyOfCallerHistoryAndRejectReoffers()
	{
		var state = new TaskGoalLinkConversationState();
		var one = new TaskGoalLinkChoice(Guid.NewGuid(), Guid.NewGuid());
		var two = new TaskGoalLinkChoice(Guid.NewGuid(), one.GoalId);
		state.Offer([one, two]); state.Decline([one.TaskId]);
		state.Pending.Should().Equal(two); state.Declined.Should().Equal(one.TaskId);
		var retry = () => state.Offer([two, one]);
		retry.Should().Throw<InvalidOperationException>().WithMessage("*declined task*");
		state.Pending.Should().Equal(two); // invalid batch is atomic
		state.Context.Should().Contain(one.TaskId.ToString()).And.Contain(two.GoalId.ToString());
		state.Applied(two.TaskId); state.Pending.Should().BeEmpty();
		state.Declined.Should().Equal(one.TaskId);
		state.Clear(); state.Context.Should().BeEmpty();
	}

	[Theory]
	[InlineData("{}")]
	[InlineData("{\"action\":\"offer\",\"choices\":[{\"taskId\":\"bad\",\"goalId\":\"bad\"}]}")]
	[InlineData("{\"action\":\"decline\",\"taskIds\":[]}")]
	[InlineData("{\"action\":\"offer\",\"choices\":[]}")]
	[InlineData("{\"action\":\"unknown\"}")]
	public void InvalidChoiceCallsLeaveStateUnchanged(string json)
	{
		var state = new TaskGoalLinkConversationState();
		var act = () => state.Apply(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json));
		act.Should().Throw<InvalidOperationException>(); state.Context.Should().BeEmpty();
	}

	[Fact]
	public void DeclineCannotInventTargetsAndMoreThanTwoGoalChoicesAreRejected()
	{
		var state = new TaskGoalLinkConversationState();
		var decline = () => state.Decline([Guid.NewGuid()]); decline.Should().Throw<InvalidOperationException>();
		var offer = () => state.Offer(Enumerable.Range(0, 3).Select(_ => new TaskGoalLinkChoice(Guid.NewGuid(), Guid.NewGuid())).ToArray());
		offer.Should().Throw<InvalidOperationException>(); state.Context.Should().BeEmpty();
	}
}

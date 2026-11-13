using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Tasks.Recurrence;

namespace OwnPlanner.Application.Tests.Tasks;

public sealed class RecurringTaskSuccessorTests
{
	[Fact]
	public void InvalidIntentOrUnrepresentableCutoffFailsBeforeAccessingPersistence()
	{
		var store = Substitute.For<IRecurringTaskStore>(); var service = new RecurringTaskSuccessors(store);
		var ct = TestContext.Current.CancellationToken;
		foreach (var request in new[]
		{
			new RecurringSuccessorRequest((RecurringSuccessorKind)99, new(2026, 11, 16)),
			new RecurringSuccessorRequest(RecurringSuccessorKind.Replace, DateOnly.MinValue),
			new RecurringSuccessorRequest(RecurringSuccessorKind.Resume, new(2026, 11, 16), EndDate: new(2026, 12, 1), ClearEndDate: true)
		})
		{
			Action preview = () => service.PreviewAsync(Guid.NewGuid(), request, ct);
			preview.Should().Throw<ArgumentException>();
			Action apply = () => service.ApplyAsync(Guid.NewGuid(), Guid.NewGuid(), request, "token", ct: ct);
			apply.Should().Throw<ArgumentException>();
		}
		store.ReceivedCalls().Should().BeEmpty();
	}

	[Fact]
	public void MissingRequestIdentityAndInvalidChoiceFailBeforeOpeningATransaction()
	{
		var store = Substitute.For<IRecurringTaskStore>(); var service = new RecurringTaskSuccessors(store);
		var request = new RecurringSuccessorRequest(RecurringSuccessorKind.Replace, new(2026, 11, 16));
		Action missing = () => service.ApplyAsync(Guid.NewGuid(), Guid.Empty, request, "token");
		missing.Should().Throw<ArgumentException>();
		Action badChoice = () => service.ApplyAsync(Guid.NewGuid(), Guid.NewGuid(), request, "token", (SuccessorConflictResolution)99);
		badChoice.Should().Throw<ArgumentException>();
		store.ReceivedCalls().Should().BeEmpty();
	}
}

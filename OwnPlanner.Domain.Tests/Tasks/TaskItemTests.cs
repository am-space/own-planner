using FluentAssertions;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Domain.Tests.Tasks;

public class TaskItemTests
{
	[Fact]
	public void Ctor_Valid_SetsProperties()
	{
		var listId = Guid.NewGuid();
		var t = new TaskItem(" title ", listId, " desc ");
		t.Title.Should().Be("title");
		t.Description.Should().Be("desc");
		t.TaskListId.Should().Be(listId);
		t.IsCompleted.Should().BeFalse();
		t.CreatedAt.Should().BeOnOrBefore(t.UpdatedAt);
	}

	[Fact]
	public void Ctor_EmptyTitle_Throws()
	{
		var listId = Guid.NewGuid();
		var act = () => new TaskItem("", listId);
		act.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void Complete_Then_Reopen_Idempotent()
	{
		var listId = Guid.NewGuid();
		var t = new TaskItem("x", listId);
		t.Complete();
		var completedAt = t.CompletedAt;

		t.Complete();
		t.CompletedAt.Should().Be(completedAt);

		t.Reopen();
		t.IsCompleted.Should().BeFalse();
		t.CompletedAt.Should().BeNull();

		t.Reopen();
		t.IsCompleted.Should().BeFalse();
	}

	[Fact]
	public void Setters_Update_UpdatedAt_Monotonically()
	{
		var listId = Guid.NewGuid();
		var t = new TaskItem("x", listId);
		var before = t.UpdatedAt;
		t.SetDescription("a");
		t.UpdatedAt.Should().BeOnOrAfter(before);
	}
}

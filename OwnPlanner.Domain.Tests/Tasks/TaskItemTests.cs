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

	[Fact]
	public void SetTitle_ValidTitle_UpdatesTitle()
	{
		var listId = Guid.NewGuid();
		var t = new TaskItem("Original Title", listId);
		var before = t.UpdatedAt;

		t.SetTitle(" New Title ");

		t.Title.Should().Be("New Title");
		t.UpdatedAt.Should().BeOnOrAfter(before);
	}

	[Fact]
	public void SetTitle_EmptyTitle_Throws()
	{
		var listId = Guid.NewGuid();
		var t = new TaskItem("Title", listId);

		var act = () => t.SetTitle("");

		act.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void SetDescription_ValidDescription_UpdatesDescription()
	{
		var listId = Guid.NewGuid();
		var t = new TaskItem("Title", listId);
		var before = t.UpdatedAt;

		t.SetDescription(" New Description ");

		t.Description.Should().Be("New Description");
		t.UpdatedAt.Should().BeOnOrAfter(before);
	}

	[Fact]
	public void SetDescription_EmptyString_SetsToNull()
	{
		var listId = Guid.NewGuid();
		var t = new TaskItem("Title", listId, "Original Description");

		t.SetDescription("   ");

		t.Description.Should().BeNull();
	}

	[Fact]
	public void SetDueAt_ValidDate_UpdatesDueDate()
	{
		var listId = Guid.NewGuid();
		var t = new TaskItem("Title", listId);
		var dueDate = new DateTime(2024, 12, 31, 10, 30, 0);
		var before = t.UpdatedAt;

		t.SetDueAt(dueDate);

		t.DueAt.Should().NotBeNull();
		t.DueAt!.Value.Year.Should().Be(2024);
		t.DueAt.Value.Month.Should().Be(12);
		t.DueAt.Value.Day.Should().Be(31);
		t.DueAt.Value.Kind.Should().Be(DateTimeKind.Utc);
		t.UpdatedAt.Should().BeOnOrAfter(before);
	}

	[Fact]
	public void SetDueAt_Null_ClearsDueDate()
	{
		var listId = Guid.NewGuid();
		var t = new TaskItem("Title", listId, dueAt: DateTime.UtcNow);

		t.SetDueAt(null);

		t.DueAt.Should().BeNull();
	}
}

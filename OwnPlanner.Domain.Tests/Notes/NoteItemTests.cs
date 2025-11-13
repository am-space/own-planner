using FluentAssertions;
using OwnPlanner.Domain.Notes;

namespace OwnPlanner.Domain.Tests.Notes;

public class NoteItemTests
{
	[Fact]
	public void Ctor_Valid_SetsProperties()
	{
		var listId = Guid.NewGuid();
		var n = new NoteItem(" My Note ", listId, " Some content ");
		
		n.Title.Should().Be("My Note");
		n.Content.Should().Be("Some content");
		n.NotesListId.Should().Be(listId);
		n.IsPinned.Should().BeFalse();
		n.CreatedAt.Should().BeOnOrBefore(n.UpdatedAt);
	}

	[Fact]
	public void Ctor_EmptyTitle_Throws()
	{
		var listId = Guid.NewGuid();
		var act = () => new NoteItem("", listId);
		act.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void Pin_Then_Unpin_Idempotent()
	{
		var listId = Guid.NewGuid();
		var n = new NoteItem("Note", listId);
		
		n.Pin();
		n.IsPinned.Should().BeTrue();
		
		n.Pin();
		n.IsPinned.Should().BeTrue();
		
		n.Unpin();
		n.IsPinned.Should().BeFalse();
		
		n.Unpin();
		n.IsPinned.Should().BeFalse();
	}

	[Fact]
	public void SetTitle_ValidTitle_UpdatesTitle()
	{
		var listId = Guid.NewGuid();
		var n = new NoteItem("Original Title", listId);
		var before = n.UpdatedAt;

		n.SetTitle(" New Title ");

		n.Title.Should().Be("New Title");
		n.UpdatedAt.Should().BeOnOrAfter(before);
	}

	[Fact]
	public void SetTitle_EmptyTitle_Throws()
	{
		var listId = Guid.NewGuid();
		var n = new NoteItem("Title", listId);

		var act = () => n.SetTitle("");

		act.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void SetContent_ValidContent_UpdatesContent()
	{
		var listId = Guid.NewGuid();
		var n = new NoteItem("Title", listId);
		var before = n.UpdatedAt;

		n.SetContent(" New Content ");

		n.Content.Should().Be("New Content");
		n.UpdatedAt.Should().BeOnOrAfter(before);
	}

	[Fact]
	public void SetContent_EmptyString_SetsToNull()
	{
		var listId = Guid.NewGuid();
		var n = new NoteItem("Title", listId, "Original Content");

		n.SetContent("   ");

		n.Content.Should().BeNull();
	}

	[Fact]
	public void AssignToList_UpdatesNotesListId()
	{
		var oldListId = Guid.NewGuid();
		var newListId = Guid.NewGuid();
		var n = new NoteItem("Note", oldListId);
		var before = n.UpdatedAt;

		n.AssignToList(newListId);

		n.NotesListId.Should().Be(newListId);
		n.UpdatedAt.Should().BeOnOrAfter(before);
	}

	[Fact]
	public void Setters_Update_UpdatedAt_Monotonically()
	{
		var listId = Guid.NewGuid();
		var n = new NoteItem("Note", listId);
		var before = n.UpdatedAt;
		
		n.SetContent("Content");
		n.UpdatedAt.Should().BeOnOrAfter(before);
		
		before = n.UpdatedAt;
		n.Pin();
		n.UpdatedAt.Should().BeOnOrAfter(before);
	}
}

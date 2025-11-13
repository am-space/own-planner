using FluentAssertions;
using OwnPlanner.Domain.Notes;

namespace OwnPlanner.Domain.Tests.Notes;

public class NotesListTests
{
	[Fact]
	public void Ctor_Valid_SetsProperties()
	{
		var nl = new NotesList(" My Notes ", " A collection of notes ", " #FF5733 ");
		
		nl.Title.Should().Be("My Notes");
		nl.Description.Should().Be("A collection of notes");
		nl.Color.Should().Be("#FF5733");
		nl.IsArchived.Should().BeFalse();
		nl.CreatedAt.Should().BeOnOrBefore(nl.UpdatedAt);
	}

	[Fact]
	public void Ctor_EmptyTitle_Throws()
	{
		var act = () => new NotesList("");
		act.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void Archive_Then_Unarchive_Idempotent()
	{
		var nl = new NotesList("Notes");
		
		nl.Archive();
		nl.IsArchived.Should().BeTrue();
		
		nl.Archive();
		nl.IsArchived.Should().BeTrue();
		
		nl.Unarchive();
		nl.IsArchived.Should().BeFalse();
		
		nl.Unarchive();
		nl.IsArchived.Should().BeFalse();
	}

	[Fact]
	public void SetTitle_ValidTitle_UpdatesTitle()
	{
		var nl = new NotesList("Original Title");
		var before = nl.UpdatedAt;

		nl.SetTitle(" New Title ");

		nl.Title.Should().Be("New Title");
		nl.UpdatedAt.Should().BeOnOrAfter(before);
	}

	[Fact]
	public void SetTitle_EmptyTitle_Throws()
	{
		var nl = new NotesList("Title");

		var act = () => nl.SetTitle("");

		act.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void SetDescription_ValidDescription_UpdatesDescription()
	{
		var nl = new NotesList("Title");
		var before = nl.UpdatedAt;

		nl.SetDescription(" New Description ");

		nl.Description.Should().Be("New Description");
		nl.UpdatedAt.Should().BeOnOrAfter(before);
	}

	[Fact]
	public void SetDescription_EmptyString_SetsToNull()
	{
		var nl = new NotesList("Title", "Original Description");

		nl.SetDescription("   ");

		nl.Description.Should().BeNull();
	}

	[Fact]
	public void SetColor_ValidColor_UpdatesColor()
	{
		var nl = new NotesList("Title");
		var before = nl.UpdatedAt;

		nl.SetColor(" #00FF00 ");

		nl.Color.Should().Be("#00FF00");
		nl.UpdatedAt.Should().BeOnOrAfter(before);
	}

	[Fact]
	public void SetColor_EmptyString_SetsToNull()
	{
		var nl = new NotesList("Title", color: "#FF0000");

		nl.SetColor("   ");

		nl.Color.Should().BeNull();
	}

	[Fact]
	public void Setters_Update_UpdatedAt_Monotonically()
	{
		var nl = new NotesList("Title");
		var before = nl.UpdatedAt;
		
		nl.SetDescription("New Description");
		nl.UpdatedAt.Should().BeOnOrAfter(before);
		
		before = nl.UpdatedAt;
		nl.SetColor("#123456");
		nl.UpdatedAt.Should().BeOnOrAfter(before);
	}
}

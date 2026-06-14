using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Notes;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Mcp.Tools.Tests;

public class NoteItemToolsTests
{
	private const int PreviewMaxLength = 200;

	private readonly INoteItemService _service = Substitute.For<INoteItemService>();
	private readonly NoteItemTools _tools;

	public NoteItemToolsTests()
	{
		_tools = new NoteItemTools(_service);
	}

	private static NoteItemDto Note(string? content) => new(
		Id: Guid.NewGuid(),
		Title: "Title",
		Content: content,
		IsPinned: false,
		CreatedAt: DateTime.UtcNow,
		UpdatedAt: DateTime.UtcNow,
		NoteListId: Guid.NewGuid(),
		GoalId: null);

	private static IReadOnlyList<NoteItemDto> AsList(object result) =>
		result.Should().BeAssignableTo<IReadOnlyList<NoteItemDto>>().Subject;

	[Fact]
	public async Task ListNotes_LongContent_IsTruncatedWithHint()
	{
		var longContent = new string('x', 500);
		_service.ListAsync(Arg.Any<CancellationToken>()).Returns([Note(longContent)]);

		var result = AsList(await _tools.ListNotes());

		var content = result.Single().Content!;
		content.Should().StartWith(new string('x', PreviewMaxLength));
		content.Should().Contain("truncated");
		content.Should().Contain("noteitem_get");
		content.Length.Should().BeLessThan(longContent.Length);
	}

	[Fact]
	public async Task ListNotes_ContentAtLimit_IsLeftUnchanged()
	{
		var content = new string('x', PreviewMaxLength);
		_service.ListAsync(Arg.Any<CancellationToken>()).Returns([Note(content)]);

		var result = AsList(await _tools.ListNotes());

		result.Single().Content.Should().Be(content);
	}

	[Fact]
	public async Task ListNotes_ShortContent_IsLeftUnchanged()
	{
		_service.ListAsync(Arg.Any<CancellationToken>()).Returns([Note("short note")]);

		var result = AsList(await _tools.ListNotes());

		result.Single().Content.Should().Be("short note");
	}

	[Fact]
	public async Task ListNotes_NullContent_IsLeftUnchanged()
	{
		_service.ListAsync(Arg.Any<CancellationToken>()).Returns([Note(null)]);

		var result = AsList(await _tools.ListNotes());

		result.Single().Content.Should().BeNull();
	}

	[Fact]
	public async Task ListNotes_WithNoteListId_ListsByNoteListAndTruncates()
	{
		var noteListId = Guid.NewGuid();
		_service.ListByNoteListAsync(noteListId, Arg.Any<CancellationToken>())
			.Returns([Note(new string('x', 500))]);

		var result = AsList(await _tools.ListNotes(noteListId));

		await _service.Received(1).ListByNoteListAsync(noteListId, Arg.Any<CancellationToken>());
		await _service.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
		result.Single().Content!.Should().Contain("truncated");
	}

	[Fact]
	public async Task ListNotesByGoal_LongContent_IsTruncated()
	{
		var goalId = Guid.NewGuid();
		_service.ListByGoalAsync(goalId, Arg.Any<CancellationToken>())
			.Returns([Note(new string('x', 500))]);

		var result = AsList(await _tools.ListNotesByGoal(goalId));

		result.Single().Content!.Should().Contain("truncated");
	}
}

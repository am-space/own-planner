using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Notes;
using OwnPlanner.Application.Notes.Interfaces;
using OwnPlanner.Domain.Notes;

namespace OwnPlanner.Application.Tests.Notes;

public class NoteItemServiceTests
{
	private readonly INoteItemRepository _repo = Substitute.For<INoteItemRepository>();
	private readonly INoteListRepository _noteListRepo = Substitute.For<INoteListRepository>();
	private readonly INoteItemService _svc;

	public NoteItemServiceTests() => _svc = new NoteItemService(_repo, _noteListRepo);

	[Fact]
	public async Task CreateAsync_Adds_And_Maps()
	{
		NoteItem? captured = null;
		var listId = Guid.NewGuid();
		var noteList = new NoteList("Test Notes");
		_noteListRepo.GetAsync(listId, Arg.Any<CancellationToken>()).Returns(noteList);
		_repo.AddAsync(Arg.Do<NoteItem>(x => captured = x), Arg.Any<CancellationToken>())
			.Returns(Task.CompletedTask);

		var dto = await _svc.CreateAsync("My Note", listId, "Some content");

		await _repo.Received(1).AddAsync(Arg.Any<NoteItem>(), Arg.Any<CancellationToken>());
		dto.Title.Should().Be("My Note");
		dto.Content.Should().Be("Some content");
		dto.NoteListId.Should().Be(listId);
		captured.Should().NotBeNull();
		dto.Id.Should().Be(captured!.Id);
	}

	[Fact]
	public async Task CreateAsync_ThrowsKeyNotFoundException_WhenNoteListNotFound()
	{
		var listId = Guid.NewGuid();
		_noteListRepo.GetAsync(listId, Arg.Any<CancellationToken>()).Returns((NoteList?)null);

		var act = async () => await _svc.CreateAsync("Note", listId, "Content");

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"NoteList {listId} not found");
	}

	[Fact]
	public async Task GetAsync_ReturnsDto_WhenFound()
	{
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var note = new NoteItem("Note", listId);
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(note);

		var dto = await _svc.GetAsync(id);

		dto.Should().NotBeNull();
		dto!.Id.Should().Be(note.Id);
		dto.Title.Should().Be("Note");
	}

	[Fact]
	public async Task GetAsync_ReturnsNull_WhenNotFound()
	{
		var id = Guid.NewGuid();
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns((NoteItem?)null);

		var dto = await _svc.GetAsync(id);

		dto.Should().BeNull();
	}

	[Fact]
	public async Task ListAsync_Maps_Notes()
	{
		var listId = Guid.NewGuid();
		var notes = new[] { new NoteItem("Note A", listId), new NoteItem("Note B", listId) }.ToList();
		_repo.ListAsync(Arg.Any<CancellationToken>()).Returns(notes);

		var result = await _svc.ListAsync();

		result.Should().HaveCount(2);
		result.Select(x => x.Title).Should().Contain(["Note A", "Note B"]);
	}

	[Fact]
	public async Task ListByNoteListAsync_Maps_Notes()
	{
		var listId = Guid.NewGuid();
		var notes = new[] { new NoteItem("Note 1", listId), new NoteItem("Note 2", listId) }.ToList();
		_repo.ListByNoteListAsync(listId, Arg.Any<CancellationToken>()).Returns(notes);

		var result = await _svc.ListByNoteListAsync(listId);

		result.Should().HaveCount(2);
		result.Should().OnlyContain(x => x.NoteListId == listId);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesTitle()
	{
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var note = new NoteItem("Old Title", listId);
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(note);

		var dto = await _svc.UpdateAsync(id, title: "New Title");

		dto.Title.Should().Be("New Title");
		await _repo.Received(1).UpdateAsync(note, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UpdateAsync_UpdatesContent()
	{
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var note = new NoteItem("Title", listId, "Old Content");
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(note);

		var dto = await _svc.UpdateAsync(id, content: "New Content");

		dto.Content.Should().Be("New Content");
		await _repo.Received(1).UpdateAsync(note, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UpdateAsync_UpdatesMultipleFields()
	{
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var note = new NoteItem("Old Title", listId, "Old Content");
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(note);

		var dto = await _svc.UpdateAsync(id, "New Title", "New Content");

		dto.Title.Should().Be("New Title");
		dto.Content.Should().Be("New Content");
		await _repo.Received(1).UpdateAsync(note, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UpdateAsync_OnlyUpdatesProvidedFields()
	{
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var note = new NoteItem("Original Title", listId, "Original Content");
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(note);

		var dto = await _svc.UpdateAsync(id, title: "New Title");

		dto.Title.Should().Be("New Title");
		dto.Content.Should().Be("Original Content");
		await _repo.Received(1).UpdateAsync(note, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UpdateAsync_ThrowsKeyNotFoundException_WhenNoteNotFound()
	{
		var id = Guid.NewGuid();
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns((NoteItem?)null);

		var act = async () => await _svc.UpdateAsync(id, title: "New Title");

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"Note {id} not found");
	}

	[Fact]
	public async Task AssignToListAsync_Updates_NoteListId()
	{
		var noteId = Guid.NewGuid();
		var oldListId = Guid.NewGuid();
		var newListId = Guid.NewGuid();
		var note = new NoteItem("Note", oldListId);
		var noteList = new NoteList("New List");
		_repo.GetAsync(noteId, Arg.Any<CancellationToken>()).Returns(note);
		_noteListRepo.GetAsync(newListId, Arg.Any<CancellationToken>()).Returns(noteList);

		await _svc.AssignToListAsync(noteId, newListId);

		note.NoteListId.Should().Be(newListId);
		await _repo.Received(1).UpdateAsync(note, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task AssignToListAsync_ThrowsKeyNotFoundException_WhenNoteNotFound()
	{
		var noteId = Guid.NewGuid();
		var listId = Guid.NewGuid();
		_repo.GetAsync(noteId, Arg.Any<CancellationToken>()).Returns((NoteItem?)null);

		var act = async () => await _svc.AssignToListAsync(noteId, listId);

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"Note {noteId} not found");
	}

	[Fact]
	public async Task AssignToListAsync_ThrowsKeyNotFoundException_WhenNoteListNotFound()
	{
		var noteId = Guid.NewGuid();
		var oldListId = Guid.NewGuid();
		var newListId = Guid.NewGuid();
		var note = new NoteItem("Note", oldListId);
		_repo.GetAsync(noteId, Arg.Any<CancellationToken>()).Returns(note);
		_noteListRepo.GetAsync(newListId, Arg.Any<CancellationToken>()).Returns((NoteList?)null);

		var act = async () => await _svc.AssignToListAsync(noteId, newListId);

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"NoteList {newListId} not found");
	}

	[Fact]
	public async Task PinAsync_Pins_Note()
	{
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var note = new NoteItem("Note", listId);
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(note);

		await _svc.PinAsync(id);

		note.IsPinned.Should().BeTrue();
		await _repo.Received(1).UpdateAsync(note, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UnpinAsync_Unpins_Note()
	{
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var note = new NoteItem("Note", listId);
		note.Pin();
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(note);

		await _svc.UnpinAsync(id);

		note.IsPinned.Should().BeFalse();
		await _repo.Received(1).UpdateAsync(note, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task DeleteAsync_Deletes_Note()
	{
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var note = new NoteItem("Note", listId);
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(note);

		await _svc.DeleteAsync(id);

		await _repo.Received(1).DeleteAsync(note, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task DeleteAsync_ThrowsKeyNotFoundException_WhenNoteNotFound()
	{
		var id = Guid.NewGuid();
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns((NoteItem?)null);

		var act = async () => await _svc.DeleteAsync(id);

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"Note {id} not found");
	}
}

using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Notes;
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
		var ct = TestContext.Current.CancellationToken;
		NoteItem? captured = null;
		var listId = Guid.NewGuid();
		var noteList = new NoteList("Test Notes");
		_noteListRepo.GetAsync(listId, ct).Returns(noteList);
		_repo.AddAsync(Arg.Do<NoteItem>(x => captured = x), ct)
			.Returns(Task.CompletedTask);

		var dto = await _svc.CreateAsync("My Note", listId, "Some content", ct: ct);

		await _repo.Received(1).AddAsync(Arg.Any<NoteItem>(), ct);
		dto.Title.Should().Be("My Note");
		dto.Content.Should().Be("Some content");
		dto.NoteListId.Should().Be(listId);
		captured.Should().NotBeNull();
		dto.Id.Should().Be(captured!.Id);
	}

	[Fact]
	public async Task CreateAsync_ThrowsKeyNotFoundException_WhenNoteListNotFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var listId = Guid.NewGuid();
		_noteListRepo.GetAsync(listId, ct).Returns((NoteList?)null);

		var act = async () => await _svc.CreateAsync("Note", listId, "Content", ct: ct);

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"NoteList {listId} not found");
	}

	[Fact]
	public async Task GetAsync_ReturnsDto_WhenFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var note = new NoteItem("Note", listId);
		_repo.GetAsync(id, ct).Returns(note);

		var dto = await _svc.GetAsync(id, ct);

		dto.Should().NotBeNull();
		dto!.Id.Should().Be(note.Id);
		dto.Title.Should().Be("Note");
	}

	[Fact]
	public async Task GetAsync_ReturnsNull_WhenNotFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		_repo.GetAsync(id, ct).Returns((NoteItem?)null);

		var dto = await _svc.GetAsync(id, ct);

		dto.Should().BeNull();
	}

	[Fact]
	public async Task ListAsync_Maps_Notes()
	{
		var ct = TestContext.Current.CancellationToken;
		var listId = Guid.NewGuid();
		var notes = new[] { new NoteItem("Note A", listId), new NoteItem("Note B", listId) }.ToList();
		_repo.ListAsync(ct).Returns(notes);

		var result = await _svc.ListAsync(ct);

		result.Should().HaveCount(2);
		result.Select(x => x.Title).Should().Contain(["Note A", "Note B"]);
	}

	[Fact]
	public async Task ListByNoteListAsync_Maps_Notes()
	{
		var ct = TestContext.Current.CancellationToken;
		var listId = Guid.NewGuid();
		var notes = new[] { new NoteItem("Note 1", listId), new NoteItem("Note 2", listId) }.ToList();
		_repo.ListByNoteListAsync(listId, ct).Returns(notes);

		var result = await _svc.ListByNoteListAsync(listId, ct);

		result.Should().HaveCount(2);
		result.Should().OnlyContain(x => x.NoteListId == listId);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesTitle()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var note = new NoteItem("Old Title", listId);
		_repo.GetAsync(id, ct).Returns(note);

		var dto = await _svc.UpdateAsync(id, title: "New Title", ct: ct);

		dto.Title.Should().Be("New Title");
		await _repo.Received(1).UpdateAsync(note, ct);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesContent()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var note = new NoteItem("Title", listId, "Old Content");
		_repo.GetAsync(id, ct).Returns(note);

		var dto = await _svc.UpdateAsync(id, content: "New Content", ct: ct);

		dto.Content.Should().Be("New Content");
		await _repo.Received(1).UpdateAsync(note, ct);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesMultipleFields()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var note = new NoteItem("Old Title", listId, "Old Content");
		_repo.GetAsync(id, ct).Returns(note);

		var dto = await _svc.UpdateAsync(id, "New Title", "New Content", ct: ct);

		dto.Title.Should().Be("New Title");
		dto.Content.Should().Be("New Content");
		await _repo.Received(1).UpdateAsync(note, ct);
	}

	[Fact]
	public async Task UpdateAsync_OnlyUpdatesProvidedFields()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var note = new NoteItem("Original Title", listId, "Original Content");
		_repo.GetAsync(id, ct).Returns(note);

		var dto = await _svc.UpdateAsync(id, title: "New Title", ct: ct);

		dto.Title.Should().Be("New Title");
		dto.Content.Should().Be("Original Content");
		await _repo.Received(1).UpdateAsync(note, ct);
	}

	[Fact]
	public async Task UpdateAsync_ThrowsKeyNotFoundException_WhenNoteNotFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		_repo.GetAsync(id, ct).Returns((NoteItem?)null);

		var act = async () => await _svc.UpdateAsync(id, title: "New Title", ct: ct);

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"Note {id} not found");
	}

	[Fact]
	public async Task AssignToListAsync_Updates_NoteListId()
	{
		var ct = TestContext.Current.CancellationToken;
		var noteId = Guid.NewGuid();
		var oldListId = Guid.NewGuid();
		var newListId = Guid.NewGuid();
		var note = new NoteItem("Note", oldListId);
		var noteList = new NoteList("New List");
		_repo.GetAsync(noteId, ct).Returns(note);
		_noteListRepo.GetAsync(newListId, ct).Returns(noteList);

		await _svc.AssignToListAsync(noteId, newListId, ct);

		note.NoteListId.Should().Be(newListId);
		await _repo.Received(1).UpdateAsync(note, ct);
	}

	[Fact]
	public async Task AssignToListAsync_ThrowsKeyNotFoundException_WhenNoteNotFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var noteId = Guid.NewGuid();
		var listId = Guid.NewGuid();
		_repo.GetAsync(noteId, ct).Returns((NoteItem?)null);

		var act = async () => await _svc.AssignToListAsync(noteId, listId, ct);

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"Note {noteId} not found");
	}

	[Fact]
	public async Task AssignToListAsync_ThrowsKeyNotFoundException_WhenNoteListNotFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var noteId = Guid.NewGuid();
		var oldListId = Guid.NewGuid();
		var newListId = Guid.NewGuid();
		var note = new NoteItem("Note", oldListId);
		_repo.GetAsync(noteId, ct).Returns(note);
		_noteListRepo.GetAsync(newListId, ct).Returns((NoteList?)null);

		var act = async () => await _svc.AssignToListAsync(noteId, newListId, ct);

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"NoteList {newListId} not found");
	}

	[Fact]
	public async Task PinAsync_Pins_Note()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var note = new NoteItem("Note", listId);
		_repo.GetAsync(id, ct).Returns(note);

		await _svc.PinAsync(id, ct);

		note.IsPinned.Should().BeTrue();
		await _repo.Received(1).UpdateAsync(note, ct);
	}

	[Fact]
	public async Task UnpinAsync_Unpins_Note()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var note = new NoteItem("Note", listId);
		note.Pin();
		_repo.GetAsync(id, ct).Returns(note);

		await _svc.UnpinAsync(id, ct);

		note.IsPinned.Should().BeFalse();
		await _repo.Received(1).UpdateAsync(note, ct);
	}

	[Fact]
	public async Task DeleteAsync_Deletes_Note()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var note = new NoteItem("Note", listId);
		_repo.GetAsync(id, ct).Returns(note);

		await _svc.DeleteAsync(id, ct);

		await _repo.Received(1).DeleteAsync(note, ct);
	}

	[Fact]
	public async Task DeleteAsync_ThrowsKeyNotFoundException_WhenNoteNotFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		_repo.GetAsync(id, ct).Returns((NoteItem?)null);

		var act = async () => await _svc.DeleteAsync(id, ct);

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"Note {id} not found");
	}

	[Fact]
	public async Task CreateAsync_SetsGoalId_InDto()
	{
		var ct = TestContext.Current.CancellationToken;
		NoteItem? captured = null;
		var listId = Guid.NewGuid();
		var goalId = Guid.NewGuid();
		var noteList = new NoteList("Test Notes");
		_noteListRepo.GetAsync(listId, ct).Returns(noteList);
		_repo.AddAsync(Arg.Do<NoteItem>(x => captured = x), ct)
			.Returns(Task.CompletedTask);

		var dto = await _svc.CreateAsync("Note", listId, goalId: goalId, ct: ct);

		dto.GoalId.Should().Be(goalId);
		captured!.GoalId.Should().Be(goalId);
	}

	[Fact]
	public async Task UpdateAsync_SetsGoalId()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var goalId = Guid.NewGuid();
		var note = new NoteItem("Title", listId);
		_repo.GetAsync(id, ct).Returns(note);

		var dto = await _svc.UpdateAsync(id, goalId: goalId, ct: ct);

		note.GoalId.Should().Be(goalId);
		dto.GoalId.Should().Be(goalId);
		await _repo.Received(1).UpdateAsync(note, ct);
	}

	[Fact]
	public async Task UpdateAsync_ClearsGoalId_WhenClearGoalIdIsTrue()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var goalId = Guid.NewGuid();
		var note = new NoteItem("Title", listId, goalId: goalId);
		_repo.GetAsync(id, ct).Returns(note);

		var dto = await _svc.UpdateAsync(id, clearGoalId: true, ct: ct);

		note.GoalId.Should().BeNull();
		dto.GoalId.Should().BeNull();
		await _repo.Received(1).UpdateAsync(note, ct);
	}

	[Fact]
	public async Task UpdateAsync_ClearGoalId_TakesPrecedenceOver_GoalId()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var newGoalId = Guid.NewGuid();
		var note = new NoteItem("Title", listId, goalId: Guid.NewGuid());
		_repo.GetAsync(id, ct).Returns(note);

		var dto = await _svc.UpdateAsync(id, goalId: newGoalId, clearGoalId: true, ct: ct);

		note.GoalId.Should().BeNull();
		dto.GoalId.Should().BeNull();
		await _repo.Received(1).UpdateAsync(note, ct);
	}

	[Fact]
	public async Task ListByGoalAsync_DelegatesToRepo_AndMapsResults()
	{
		var ct = TestContext.Current.CancellationToken;
		var listId = Guid.NewGuid();
		var goalId = Guid.NewGuid();
		var notes = new[]
		{
			new NoteItem("a", listId, goalId: goalId),
			new NoteItem("b", listId, goalId: goalId)
		}.ToList();
		_repo.ListByGoalAsync(goalId, ct).Returns(notes);

		var result = await _svc.ListByGoalAsync(goalId, ct);

		result.Should().HaveCount(2);
		result.Should().OnlyContain(x => x.GoalId == goalId);
	}
}

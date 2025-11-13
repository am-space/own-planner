using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Notes;
using OwnPlanner.Application.Notes.Interfaces;
using OwnPlanner.Domain.Notes;

namespace OwnPlanner.Application.Tests.Notes;

public class NoteListServiceTests
{
	private readonly INoteListRepository _repo = Substitute.For<INoteListRepository>();
	private readonly INoteListService _svc;

	public NoteListServiceTests() => _svc = new NoteListService(_repo);

	[Fact]
	public async Task CreateAsync_Adds_And_Maps()
	{
		NoteList? captured = null;
		_repo.AddAsync(Arg.Do<NoteList>(x => captured = x), Arg.Any<CancellationToken>())
			.Returns(Task.CompletedTask);

		var dto = await _svc.CreateAsync("My Notes", "A collection", "#FF5733");

		await _repo.Received(1).AddAsync(Arg.Any<NoteList>(), Arg.Any<CancellationToken>());
		dto.Title.Should().Be("My Notes");
		dto.Description.Should().Be("A collection");
		dto.Color.Should().Be("#FF5733");
		dto.IsArchived.Should().BeFalse();
		captured.Should().NotBeNull();
		dto.Id.Should().Be(captured!.Id);
	}

	[Fact]
	public async Task GetAsync_ReturnsDto_WhenFound()
	{
		var id = Guid.NewGuid();
		var noteList = new NoteList("Notes");
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(noteList);

		var dto = await _svc.GetAsync(id);

		dto.Should().NotBeNull();
		dto!.Id.Should().Be(noteList.Id);
		dto.Title.Should().Be("Notes");
	}

	[Fact]
	public async Task GetAsync_ReturnsNull_WhenNotFound()
	{
		var id = Guid.NewGuid();
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns((NoteList?)null);

		var dto = await _svc.GetAsync(id);

		dto.Should().BeNull();
	}

	[Fact]
	public async Task ListAsync_Maps_Lists()
	{
		var lists = new[] { new NoteList("Personal"), new NoteList("Work") }.ToList();
		_repo.ListAsync(false, Arg.Any<CancellationToken>()).Returns(lists);

		var result = await _svc.ListAsync(false);

		result.Should().HaveCount(2);
		result.Select(x => x.Title).Should().Contain(["Personal", "Work"]);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesTitle()
	{
		var id = Guid.NewGuid();
		var noteList = new NoteList("Old Title");
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(noteList);

		var dto = await _svc.UpdateAsync(id, title: "New Title");

		dto.Title.Should().Be("New Title");
		await _repo.Received(1).UpdateAsync(noteList, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UpdateAsync_UpdatesDescription()
	{
		var id = Guid.NewGuid();
		var noteList = new NoteList("Title", "Old Description");
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(noteList);

		var dto = await _svc.UpdateAsync(id, description: "New Description");

		dto.Description.Should().Be("New Description");
		await _repo.Received(1).UpdateAsync(noteList, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UpdateAsync_UpdatesColor()
	{
		var id = Guid.NewGuid();
		var noteList = new NoteList("Title");
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(noteList);

		var dto = await _svc.UpdateAsync(id, color: "#00FF00");

		dto.Color.Should().Be("#00FF00");
		await _repo.Received(1).UpdateAsync(noteList, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UpdateAsync_UpdatesMultipleFields()
	{
		var id = Guid.NewGuid();
		var noteList = new NoteList("Old Title", "Old Description");
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(noteList);

		var dto = await _svc.UpdateAsync(id, "New Title", "New Description", "#FF0000");

		dto.Title.Should().Be("New Title");
		dto.Description.Should().Be("New Description");
		dto.Color.Should().Be("#FF0000");
		await _repo.Received(1).UpdateAsync(noteList, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UpdateAsync_OnlyUpdatesProvidedFields()
	{
		var id = Guid.NewGuid();
		var noteList = new NoteList("Original Title", "Original Description");
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(noteList);

		var dto = await _svc.UpdateAsync(id, title: "New Title");

		dto.Title.Should().Be("New Title");
		dto.Description.Should().Be("Original Description");
		await _repo.Received(1).UpdateAsync(noteList, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UpdateAsync_ThrowsKeyNotFoundException_WhenNotFound()
	{
		var id = Guid.NewGuid();
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns((NoteList?)null);

		var act = async () => await _svc.UpdateAsync(id, title: "New Title");

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"NoteList {id} not found");
	}

	[Fact]
	public async Task ArchiveAsync_Gets_Updates()
	{
		var id = Guid.NewGuid();
		var noteList = new NoteList("Notes");
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(noteList);

		await _svc.ArchiveAsync(id);

		noteList.IsArchived.Should().BeTrue();
		await _repo.Received(1).UpdateAsync(noteList, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UnarchiveAsync_Gets_Updates()
	{
		var id = Guid.NewGuid();
		var noteList = new NoteList("Notes");
		noteList.Archive();
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(noteList);

		await _svc.UnarchiveAsync(id);

		noteList.IsArchived.Should().BeFalse();
		await _repo.Received(1).UpdateAsync(noteList, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task DeleteAsync_Gets_Deletes()
	{
		var id = Guid.NewGuid();
		var noteList = new NoteList("Notes");
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(noteList);

		await _svc.DeleteAsync(id);

		await _repo.Received(1).DeleteAsync(noteList, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task DeleteAsync_ThrowsKeyNotFoundException_WhenNotFound()
	{
		var id = Guid.NewGuid();
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns((NoteList?)null);

		var act = async () => await _svc.DeleteAsync(id);

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"NoteList {id} not found");
	}
}

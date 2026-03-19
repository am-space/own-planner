using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Notes;
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
		var ct = TestContext.Current.CancellationToken;
		NoteList? captured = null;
		_repo.AddAsync(Arg.Do<NoteList>(x => captured = x), ct)
			.Returns(Task.CompletedTask);

		var contextId = Guid.NewGuid();
		var dto = await _svc.CreateAsync("My Notes", contextId, "A collection", "#FF5733", ct);

		await _repo.Received(1).AddAsync(Arg.Any<NoteList>(), ct);
		dto.Title.Should().Be("My Notes");
		dto.Description.Should().Be("A collection");
		dto.Color.Should().Be("#FF5733");
		dto.IsArchived.Should().BeFalse();
		dto.ContextId.Should().Be(contextId);
		captured.Should().NotBeNull();
		dto.Id.Should().Be(captured!.Id);
	}

	[Fact]
	public async Task GetAsync_ReturnsDto_WhenFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var noteList = new NoteList("Notes");
		_repo.GetAsync(id, ct).Returns(noteList);

		var dto = await _svc.GetAsync(id, ct);

		dto.Should().NotBeNull();
		dto!.Id.Should().Be(noteList.Id);
		dto.Title.Should().Be("Notes");
	}

	[Fact]
	public async Task GetAsync_ReturnsNull_WhenNotFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		_repo.GetAsync(id, ct).Returns((NoteList?)null);

		var dto = await _svc.GetAsync(id, ct);

		dto.Should().BeNull();
	}

	[Fact]
	public async Task ListAsync_Maps_Lists()
	{
		var ct = TestContext.Current.CancellationToken;
		var ctxId = Guid.NewGuid();
		var lists = new[] { new NoteList("Personal", contextId: ctxId), new NoteList("Work", contextId: ctxId) }.ToList();
		_repo.ListAsync(false, null, false, ct).Returns(lists);

		var result = await _svc.ListAsync(false, ct: ct);

		result.Should().HaveCount(2);
		result.Select(x => x.Title).Should().Contain(["Personal", "Work"]);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesTitle()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var noteList = new NoteList("Old Title");
		_repo.GetAsync(id, ct).Returns(noteList);

		var dto = await _svc.UpdateAsync(id, title: "New Title", ct: ct);

		dto.Title.Should().Be("New Title");
		await _repo.Received(1).UpdateAsync(noteList, ct);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesDescription()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var noteList = new NoteList("Title", "Old Description");
		_repo.GetAsync(id, ct).Returns(noteList);

		var dto = await _svc.UpdateAsync(id, description: "New Description", ct: ct);

		dto.Description.Should().Be("New Description");
		await _repo.Received(1).UpdateAsync(noteList, ct);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesColor()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var noteList = new NoteList("Title");
		_repo.GetAsync(id, ct).Returns(noteList);

		var dto = await _svc.UpdateAsync(id, color: "#00FF00", ct: ct);

		dto.Color.Should().Be("#00FF00");
		await _repo.Received(1).UpdateAsync(noteList, ct);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesContextId()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var contextId = Guid.NewGuid();
		var noteList = new NoteList("Title");
		_repo.GetAsync(id, ct).Returns(noteList);

		var dto = await _svc.UpdateAsync(id, contextId: contextId, ct: ct);

		dto.ContextId.Should().Be(contextId);
		await _repo.Received(1).UpdateAsync(noteList, ct);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesMultipleFields()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var contextId = Guid.NewGuid();
		var noteList = new NoteList("Old Title", "Old Description");
		_repo.GetAsync(id, ct).Returns(noteList);

		var dto = await _svc.UpdateAsync(id, "New Title", contextId, "New Description", "#FF0000", ct);

		dto.Title.Should().Be("New Title");
		dto.Description.Should().Be("New Description");
		dto.Color.Should().Be("#FF0000");
		dto.ContextId.Should().Be(contextId);
		await _repo.Received(1).UpdateAsync(noteList, ct);
	}

	[Fact]
	public async Task UpdateAsync_OnlyUpdatesProvidedFields()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var noteList = new NoteList("Original Title", "Original Description");
		_repo.GetAsync(id, ct).Returns(noteList);

		var dto = await _svc.UpdateAsync(id, title: "New Title", ct: ct);

		dto.Title.Should().Be("New Title");
		dto.Description.Should().Be("Original Description");
		await _repo.Received(1).UpdateAsync(noteList, ct);
	}

	[Fact]
	public async Task CreateAsync_EmptyContextId_ThrowsArgumentException()
	{
		var ct = TestContext.Current.CancellationToken;
		var act = async () => await _svc.CreateAsync("My Notes", Guid.Empty, ct: ct);

		await act.Should().ThrowAsync<ArgumentException>().WithParameterName("contextId");
	}

	[Fact]
	public async Task UpdateAsync_ThrowsKeyNotFoundException_WhenNotFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		_repo.GetAsync(id, ct).Returns((NoteList?)null);

		var act = async () => await _svc.UpdateAsync(id, title: "New Title", ct: ct);

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"NoteList {id} not found");
	}

	[Fact]
	public async Task ArchiveAsync_Gets_Updates()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var noteList = new NoteList("Notes");
		_repo.GetAsync(id, ct).Returns(noteList);

		await _svc.ArchiveAsync(id, ct);

		noteList.IsArchived.Should().BeTrue();
		await _repo.Received(1).UpdateAsync(noteList, ct);
	}

	[Fact]
	public async Task UnarchiveAsync_Gets_Updates()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var noteList = new NoteList("Notes");
		noteList.Archive();
		_repo.GetAsync(id, ct).Returns(noteList);

		await _svc.UnarchiveAsync(id, ct);

		noteList.IsArchived.Should().BeFalse();
		await _repo.Received(1).UpdateAsync(noteList, ct);
	}

	[Fact]
	public async Task DeleteAsync_Gets_Deletes()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var noteList = new NoteList("Notes");
		_repo.GetAsync(id, ct).Returns(noteList);

		await _svc.DeleteAsync(id, ct);

		await _repo.Received(1).DeleteAsync(noteList, ct);
	}

	[Fact]
	public async Task DeleteAsync_ThrowsKeyNotFoundException_WhenNotFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		_repo.GetAsync(id, ct).Returns((NoteList?)null);

		var act = async () => await _svc.DeleteAsync(id, ct);

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"NoteList {id} not found");
	}
}

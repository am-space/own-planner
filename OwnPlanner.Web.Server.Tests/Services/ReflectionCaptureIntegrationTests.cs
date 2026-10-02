using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OwnPlanner.Application.Notes;
using OwnPlanner.Domain;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed partial class DirectToolMcpAdapterTests
{
	[Fact]
	public async Task CaptureTaskPreservesSourceAndIndependentNoteMovementRemainsTenantBound()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var services = BuildTenantServiceProvider(s =>
		{
			s.AddScoped<INoteItemRepository, NoteItemRepository>();
			s.AddScoped<INoteListRepository, NoteListRepository>();
			s.AddScoped<INoteItemService, NoteItemService>();
			s.AddScoped<INoteListService, NoteListService>();
		});
		var (noteA, destinationA) = await SeedCaptureWorkspaceAsync("capture-a", "A capture", ct);
		var (noteB, destinationB) = await SeedCaptureWorkspaceAsync("capture-b", "B private", ct);
		await using var a = CreateAdapter(services, "capture-a");
		await using var b = CreateAdapter(services, "capture-b");
		var before = ParseJsonElement(await a.CallToolAsync("noteitem_get", new Dictionary<string, object?> { ["id"] = noteA }, ct));
		var lists = ParseJsonElement(await a.CallToolAsync("tasklist_all", new Dictionary<string, object?> { ["includeUnassigned"] = true }, ct));
		var inbox = lists.EnumerateArray().Single(l => l.GetProperty("isSystem").GetBoolean());
		inbox.GetProperty("id").GetGuid().Should().Be(WellKnownIds.InboxTaskList);
		var noteLists = ParseJsonElement(await a.CallToolAsync("notelist_all", new Dictionary<string, object?> { ["includeUnassigned"] = true }, ct));
		noteLists.EnumerateArray().Should().Contain(l => l.GetProperty("id").GetGuid() == destinationA)
			.And.NotContain(l => l.GetProperty("id").GetGuid() == destinationB);
		var created = ParseJsonElement(await a.CallToolAsync("taskitem_create", new Dictionary<string, object?>
			{ ["title"] = "Act on A capture", ["description"] = before.GetProperty("content").GetString(), ["taskListId"] = inbox.GetProperty("id").GetGuid() }, ct));
		created.GetProperty("taskListId").GetGuid().Should().Be(WellKnownIds.InboxTaskList);
		var after = ParseJsonElement(await a.CallToolAsync("noteitem_get", new Dictionary<string, object?> { ["id"] = noteA }, ct));
		after.ToString().Should().Be(before.ToString()); // creation does not touch, move or remove the source
		var foreignDestination = ParseJsonElement(await a.CallToolAsync("noteitem_assign", new Dictionary<string, object?>
			{ ["noteId"] = noteA, ["noteListId"] = destinationB }, ct));
		foreignDestination.GetProperty("error").GetString().Should().Contain("not found").And.NotContain("B private");
		var unchanged = ParseJsonElement(await a.CallToolAsync("noteitem_get", new Dictionary<string, object?> { ["id"] = noteA }, ct));
		unchanged.ToString().Should().Be(before.ToString());
		var foreignNote = ParseJsonElement(await a.CallToolAsync("noteitem_assign", new Dictionary<string, object?>
			{ ["noteId"] = noteB, ["noteListId"] = destinationA }, ct));
		foreignNote.GetProperty("error").GetString().Should().Contain("not found");
		var stillCreated = ParseJsonElement(await a.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = created.GetProperty("id").GetGuid() }, ct));
		stillCreated.GetProperty("title").GetString().Should().Be("Act on A capture");
		var moved = ParseJsonElement(await a.CallToolAsync("noteitem_assign", new Dictionary<string, object?>
			{ ["noteId"] = noteA, ["noteListId"] = destinationA }, ct));
		moved.GetProperty("success").GetBoolean().Should().BeTrue();
		var source = ParseJsonElement(await a.CallToolAsync("noteitem_get", new Dictionary<string, object?> { ["id"] = noteA }, ct));
		source.GetProperty("noteListId").GetGuid().Should().Be(destinationA);
		source.GetProperty("content").GetString().Should().Be(before.GetProperty("content").GetString());
		var other = ParseJsonElement(await b.CallToolAsync("noteitem_get", new Dictionary<string, object?> { ["id"] = noteB }, ct));
		other.GetProperty("noteListId").GetGuid().Should().Be(WellKnownIds.InboxNoteList);
		var otherTasks = ParseJsonElement(await b.CallToolAsync("taskitem_list_items", cancellationToken: ct));
		otherTasks.GetProperty("totalCount").GetInt32().Should().Be(0);
	}

	private async Task<(Guid Note, Guid Destination)> SeedCaptureWorkspaceAsync(string userId, string title, CancellationToken ct)
	{
		var path = Path.Combine(_tempDirectory, $"ownplanner-user-{userId}.db");
		await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options);
		await db.Database.MigrateAsync(ct);
		db.AddRange(TaskList.CreateSystem(WellKnownIds.InboxTaskList, "Inbox"), NoteList.CreateSystem(WellKnownIds.InboxNoteList, "Inbox"));
		var destination = new NoteList("Reference");
		var capture = new NoteItem(title, WellKnownIds.InboxNoteList, "Full original capture content");
		db.AddRange(destination, capture);
		await db.SaveChangesAsync(ct);
		return (capture.Id, destination.Id);
	}
}

using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using OwnPlanner.Application.Chat;
using OwnPlanner.Domain;
using OwnPlanner.E2E.Tests.Infrastructure;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.E2E.Tests;

[Collection(E2eCollection.Name)]
[Trait("Category", "E2E")]
public sealed class RecurringPlannerE2eTests(E2eWebApplicationFactory application) : E2ePageTest(application)
{
	[Theory]
	[InlineData(1440)]
	[InlineData(390)]
	public async Task SeriesWorkspace_ShowsOneRowAndLinkedOutstandingWork_AndRestoresWithoutResuming(int width)
	{
		await Page.SetViewportSizeAsync(width, 900);
		await RegisterAsync(Page, CreateUser());
		var prefix = $"Series workspace {Guid.NewGuid():N}";
		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		Guid previousId = default, successorId = default;
		var prompt = Application.Scenarios.Register(async adapter =>
		{
			(previousId, successorId) = await SeedLinkedSeries(adapter!, prefix, today);
			await Call(adapter!, "taskitem_create", new() { ["title"] = $"{prefix} Ordinary", ["taskListId"] = WellKnownIds.InboxTaskList });
			return new ChatTurnResult("Prepared linked recurring work", 100);
		});
		await SendPromptAsync(Page, prompt);
		await Expect(Page.GetByText("Prepared linked recurring work", new() { Exact = true })).ToBeVisibleAsync();
		await Page.GotoAsync($"/planner/tasks?search={Uri.EscapeDataString(prefix)}");
		await Expect(Page.GetByText("1–3 of 3", new() { Exact = true })).ToBeVisibleAsync();
		await Expect(Page.Locator("[id^='planner-row-']")).ToHaveCountAsync(3);
		await Expect(Page.GetByText("Recurring series", new() { Exact = true })).ToHaveCountAsync(2);
		await Expect(Page.GetByText("Previous series has unfinished work", new() { Exact = true })).ToBeVisibleAsync();
		await Page.GetByLabel("Search tasks").FillAsync($"{prefix} Monthly");
		await Page.GetByText($"{prefix} Monthly", new() { Exact = true }).ClickAsync();
		await Expect(Page.GetByText("Every 1 month on days 31 (clamped to month end)", new() { Exact = true }).Last).ToBeVisibleAsync();
		await Expect(Page.GetByText("Retain each obligation", new() { Exact = true })).ToBeVisibleAsync();
		await Expect(Page.GetByText("Series starts", new() { Exact = true })).ToBeVisibleAsync();
		await Expect(Page.GetByText(today.AddDays(1).ToString("yyyy-MM-dd"), new() { Exact = true })).ToBeVisibleAsync();
		await Page.GetByRole(AriaRole.Button, new() { Name = "View previous series", Exact = true }).ClickAsync();
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"{prefix} Weekly", Exact = true })).ToBeVisibleAsync();
		await Expect(Page.GetByText("This earlier series still has unfinished occurrences.", new() { Exact = false })).ToBeVisibleAsync();
		await Expect(Page.GetByText(today.ToString("yyyy-MM-dd"), new() { Exact = true })).ToBeVisibleAsync();
		await Page.GetByRole(AriaRole.Button, new() { Name = "View successor series", Exact = true }).ClickAsync();
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"{prefix} Monthly", Exact = true })).ToBeVisibleAsync();
		await Page.ReloadAsync();
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"{prefix} Monthly", Exact = true })).ToBeVisibleAsync();

		await Page.GotoAsync("/chat");
		prompt = Application.Scenarios.Register(async adapter =>
		{
			await Call(adapter!, "taskitem_delete", new() { ["id"] = previousId });
			return new ChatTurnResult("Trashed earlier series with history intact", 100);
		});
		await SendPromptAsync(Page, prompt);
		await Expect(Page.GetByText("Trashed earlier series with history intact", new() { Exact = true })).ToBeVisibleAsync();
		await Page.GotoAsync("/planner/trash");
		await Expect(Page.GetByText("Restoring a recurring series preserves its history and keeps it ended", new() { Exact = false })).ToBeVisibleAsync();
		await Page.Locator("li").Filter(new() { HasText = $"{prefix} Weekly" }).GetByRole(AriaRole.Button, new() { Name = "Restore", Exact = true }).ClickAsync();
		await Expect(Page.GetByText($"{prefix} Weekly", new() { Exact = true })).ToHaveCountAsync(0);
		await Page.GotoAsync($"/planner/tasks?status=Completed&selected={previousId}");
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"{prefix} Weekly", Exact = true })).ToBeVisibleAsync();
		await Expect(Page.GetByText("Series ended", new() { Exact = true }).Last).ToBeVisibleAsync();
		await Expect(Page.GetByText("Unfinished earlier series", new() { Exact = true })).ToHaveCountAsync(0);
		await Page.GotoAsync("/chat");
		prompt = Application.Scenarios.Register(async adapter =>
		{
			var history = await Call(adapter!, "task_occurrence_history", new() { ["basis"] = "ScheduledDate", ["taskId"] = previousId,
				["startDate"] = D(today.AddDays(-14)), ["endExclusiveDate"] = D(today.AddDays(15)) });
			history.GetProperty("totalCount").GetInt32().Should().Be(2);
			var old = await Call(adapter!, "recurring_task_get", new() { ["taskId"] = previousId });
			old.GetProperty("successorTaskId").GetGuid().Should().Be(successorId);
			return new ChatTurnResult("Recovered original facts and links without resuming", 100);
		});
		await SendPromptAsync(Page, prompt);
		await Expect(Page.GetByText("Recovered original facts and links without resuming", new() { Exact = true })).ToBeVisibleAsync();
	}

	[Fact]
	public async Task AccountExportAndDeletion_PreserveOnlyTheAuthenticatedUsersSeriesAndFacts()
	{
		var ct = TestContext.Current.CancellationToken;
		var userA = CreateUser(); await RegisterAsync(Page, userA);
		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		Guid oldA = default, nextA = default, oldB = default, nextB = default;
		async Task Seed(IPage page, string title, Action<Guid, Guid> capture)
		{
			var prompt = Application.Scenarios.Register(async adapter =>
			{
				var (old, next) = await SeedLinkedSeries(adapter!, title, today); capture(old, next);
				return new ChatTurnResult("Prepared account recurrence", 100);
			});
			await SendPromptAsync(page, prompt);
			await Expect(page.GetByText("Prepared account recurrence", new() { Exact = true })).ToBeVisibleAsync();
		}
		await Seed(Page, "Private account A", (a, b) => (oldA, nextA) = (a, b));
		await using var second = await Browser.NewContextAsync(CreateContextOptions()); var pageB = await second.NewPageAsync();
		var userB = CreateUser(); await RegisterAsync(pageB, userB);
		await Seed(pageB, "Private account B", (a, b) => (oldB, nextB) = (a, b));
		(await Context.APIRequest.GetAsync($"/api/planner/tasks/{oldB}")).Status.Should().Be(404);
		(await second.APIRequest.GetAsync($"/api/planner/tasks/{nextA}")).Status.Should().Be(404);
		var page = await (await second.APIRequest.GetAsync("/api/planner/tasks?status=all")).TextAsync();
		page.Should().NotContain(oldA.ToString()).And.NotContain(nextA.ToString()).And.NotContain("Private account A");
		var directory = Path.Combine(Path.GetTempPath(), $"ownplanner-series-export-{Guid.NewGuid():N}"); Directory.CreateDirectory(directory);
		try
		{
			async Task VerifyExport(IBrowserContext browser, Guid old, Guid next, Guid foreign)
			{
				var response = await browser.APIRequest.GetAsync("/api/account/export"); response.Status.Should().Be(200);
				using var zip = new ZipArchive(new MemoryStream(await response.BodyAsync()));
				var file = Path.Combine(directory, $"{old}.db"); zip.GetEntry("ownplanner-data.db")!.ExtractToFile(file);
				await using var snapshot = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={file};Pooling=False").Options);
				var source = await snapshot.TaskItems.SingleAsync(t => t.Id == old, ct);
				source.Recurrence!.EndDate.Should().Be(today); source.RecurrenceSuccessorTaskId.Should().Be(next);
				(await snapshot.TaskItems.SingleAsync(t => t.Id == next, ct)).RecurrencePredecessorTaskId.Should().Be(old);
				(await snapshot.TaskItems.AnyAsync(t => t.Id == foreign, ct)).Should().BeFalse();
				var facts = await snapshot.TaskOccurrenceFacts.Where(f => f.TaskId == old).OrderBy(f => f.OccurrenceDate).ToArrayAsync(ct);
				facts.Should().HaveCount(2); facts[0].OccurrenceDate.Should().Be(today.AddDays(-14));
				facts[1].OccurrenceDate.Should().Be(today.AddDays(14)); // history outside shortened bounds survives export
				facts.Should().OnlyContain(f => f.CompletedAt != null);
				(await snapshot.RecurringSuccessorOperations.CountAsync(ct)).Should().Be(1);
				(await snapshot.Database.GetPendingMigrationsAsync(ct)).Should().BeEmpty();
			}
			await VerifyExport(Context, oldA, nextA, oldB); await VerifyExport(second, oldB, nextB, oldA);
			Guid userIdA;
			await using (var auth = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite($"Data Source={Application.AuthDatabasePath}").Options))
				userIdA = await auth.Users.Where(u => u.Email == userA.Email).Select(u => u.Id).SingleAsync(ct);
			var deleted = await Context.APIRequest.PostAsync("/api/account/delete", new() { DataObject = new { password = userA.Password } });
			deleted.Status.Should().Be(200);
			File.Exists(Path.Combine(Application.UserDatabaseDirectory, $"ownplanner-user-{userIdA}.db")).Should().BeFalse();
			(await Context.APIRequest.GetAsync("/api/planner/tasks")).Status.Should().Be(401);
			(await second.APIRequest.GetAsync($"/api/planner/tasks/{oldB}")).Status.Should().Be(200);
			await using var remaining = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite($"Data Source={Application.AuthDatabasePath}").Options);
			(await remaining.Users.AnyAsync(u => u.Email == userA.Email, ct)).Should().BeFalse();
			(await remaining.Users.AnyAsync(u => u.Email == userB.Email, ct)).Should().BeTrue();
		}
		finally { Directory.Delete(directory, true); }
	}

	private static string D(DateOnly value) => value.ToString("yyyy-MM-dd");
	private static async Task<JsonElement> Call(IMcpAdapter adapter, string name, Dictionary<string, object?> args)
	{
		using var result = JsonDocument.Parse(await adapter.CallToolAsync(name, args));
		result.RootElement.TryGetProperty("error", out var error).Should().BeFalse($"{name}: {error}");
		return result.RootElement.Clone();
	}
	private static async Task<(Guid Old, Guid Next)> SeedLinkedSeries(IMcpAdapter adapter, string title, DateOnly today)
	{
		var series = await Call(adapter, "recurring_task_create", new() { ["title"] = $"{title} Weekly", ["taskListId"] = WellKnownIds.InboxTaskList,
			["startDate"] = D(today.AddDays(-14)), ["frequency"] = "Weekly", ["weekdays"] = new[] { today.DayOfWeek.ToString() }, ["missedPolicy"] = "RetainEachObligation" });
		var id = series.GetProperty("task").GetProperty("id").GetGuid();
		await Call(adapter, "task_occurrence_complete", new() { ["taskId"] = id, ["occurrenceDate"] = D(today.AddDays(-14)), ["completedAt"] = DateTime.UtcNow.AddDays(-3).ToString("O") });
		await Call(adapter, "task_occurrence_complete", new() { ["taskId"] = id, ["occurrenceDate"] = D(today.AddDays(14)) });
		var change = new Dictionary<string, object?> { ["taskId"] = id, ["kind"] = "Replace", ["startDate"] = D(today.AddDays(1)), ["frequency"] = "Monthly", ["monthDays"] = new[] { 31 } };
		var preview = await Call(adapter, "recurring_task_successor_preview", change);
		preview.GetProperty("affectedFacts").GetArrayLength().Should().Be(1);
		change["previewToken"] = preview.GetProperty("previewToken").GetString(); change["requestId"] = Guid.NewGuid(); change["resolution"] = "KeepStartAsSeparateWork";
		var result = await Call(adapter, "recurring_task_successor_apply", change);
		var next = result.GetProperty("successorTaskId").GetGuid();
		await Call(adapter, "taskitem_update", new() { ["id"] = next, ["title"] = $"{title} Monthly" });
		return (id, next);
	}
}

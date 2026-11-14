using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed partial class DirectToolMcpAdapterTests
{
	[Fact]
	public async Task ReviewOccurrencePagesAndGuardedActionsStayInsideAuthenticatedTenant()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var services = BuildTenantServiceProvider();
		async Task<Guid> Seed(string userId, string title)
		{
			var path = Path.Combine(_tempDirectory, $"ownplanner-user-{userId}.db");
			await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options);
			await db.Database.MigrateAsync(ct);
			var list = new TaskList("Review series"); var task = new TaskItem(title, list.Id, "PRIVATE DESCRIPTION");
			task.ConfigureRecurrence(new(RecurrenceRule.Weekly(1, Enum.GetValues<DayOfWeek>(), DayOfWeek.Monday),
				DateOnly.FromDateTime(TenantTestUtcNow).AddDays(-7), null, MissedOccurrencePolicy.RetainEachObligation));
			db.AddRange(list, task); await db.SaveChangesAsync(ct); return task.Id;
		}
		var own = await Seed("review-a", "Own review recurrence"); var foreign = await Seed("review-b", "Foreign recurrence");
		await using var a = CreateAdapter(services, "review-a"); await using var b = CreateAdapter(services, "review-b");
		foreach (var adapter in new[] { a, b })
			await adapter.CallToolAsync("weekly_review_configure", new Dictionary<string, object?> { ["enabled"] = false, ["timeZoneId"] = "UTC" }, ct);
		var view = ParseJsonElement(await a.CallToolAsync("weekly_review_open", cancellationToken: ct));
		view.ToString().Should().Contain("Own review recurrence").And.NotContain("Foreign recurrence").And.NotContain("PRIVATE DESCRIPTION");
		var reviewId = view.GetProperty("review").GetProperty("id").GetGuid();
		var foreignRead = () => b.CallToolAsync("weekly_review_occurrences", new Dictionary<string, object?> { ["reviewId"] = reviewId }, ct);
		await foreignRead.Should().ThrowAsync<KeyNotFoundException>();
		var details = ParseJsonElement(await a.CallToolAsync("weekly_review_occurrences", new Dictionary<string, object?>
			{ ["reviewId"] = reviewId, ["taskId"] = own, ["offset"] = 1, ["limit"] = 1 }, ct));
		details.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(1);
		var row = details.GetProperty("items")[0]; var work = row.GetProperty("work");
		var args = new Dictionary<string, object?>
		{
			["reviewId"] = reviewId, ["taskId"] = foreign, ["revision"] = row.GetProperty("revision").GetString(),
			["occurrenceDate"] = work.GetProperty("occurrenceDate").GetString(), ["guardToken"] = row.GetProperty("guardToken").GetString(),
			["action"] = "completeOccurrence", ["completedAt"] = new DateTimeOffset(TenantTestUtcNow.AddHours(-2)).ToOffset(TimeSpan.FromHours(-4)).ToString("O")
		};
		ParseJsonElement(await a.CallToolAsync("weekly_review_apply", args, ct)).GetProperty("applied").GetBoolean().Should().BeFalse();
		args["taskId"] = own;
		var result = ParseJsonElement(await a.CallToolAsync("weekly_review_apply", args, ct));
		result.GetProperty("applied").GetBoolean().Should().BeTrue();
		result.GetProperty("occurrence").GetProperty("id").GetProperty("taskId").GetGuid().Should().Be(own);
		result.GetProperty("occurrence").GetProperty("completedAt").GetDateTime().Should().Be(TenantTestUtcNow.AddHours(-2));
		ParseJsonElement(await a.CallToolAsync("weekly_review_apply", args, ct)).GetProperty("applied").GetBoolean().Should().BeFalse();
		foreach (var (user, id, expected) in new[] { ("review-a", own, 1), ("review-b", foreign, 0) })
		{
			await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
				.UseSqlite($"Data Source={Path.Combine(_tempDirectory, $"ownplanner-user-{user}.db")}").Options);
			(await db.TaskOccurrenceFacts.CountAsync(ct)).Should().Be(expected);
			(await db.TaskItems.FindAsync([id], ct))!.IsCompleted.Should().BeFalse();
		}
	}
}

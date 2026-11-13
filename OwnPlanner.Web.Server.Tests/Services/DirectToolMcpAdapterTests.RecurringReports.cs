using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OwnPlanner.Application.Chat;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed partial class DirectToolMcpAdapterTests
{
	[Theory]
	[InlineData(PlanningMode.General)]
	[InlineData(PlanningMode.DayWork)]
	[InlineData(PlanningMode.WeekPlanning)]
	[InlineData(PlanningMode.Reflection)]
	[InlineData(PlanningMode.GlobalPlanning)]
	[InlineData(PlanningMode.SystemAnalysis)]
	public async Task EveryModePreloadIncludesOriginalOccurrenceIdentitiesAndOnlyItsOwnTenant(PlanningMode mode)
	{
		var ct = TestContext.Current.CancellationToken;
		await using var services = BuildTenantServiceProvider();
		async Task<Guid> SeedSeries(string userId, string title)
		{
			var path = Path.Combine(_tempDirectory, $"ownplanner-user-{userId}.db");
			await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options);
			await db.Database.MigrateAsync(ct);
			var list = new TaskList("Recurring work");
			var task = new TaskItem(title, list.Id);
			task.ConfigureRecurrence(new(RecurrenceRule.Weekly(1, [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday], DayOfWeek.Monday),
				new(2026, 8, 10), new(2026, 8, 23), MissedOccurrencePolicy.RetainEachObligation));
			db.AddRange(list, task);
			db.TaskOccurrenceFacts.Add(new() { TaskId = task.Id, OccurrenceDate = new(2026, 8, 14), Status = TaskOccurrenceStatus.Completed, CompletedAt = TenantTestUtcNow.AddDays(-7) });
			await db.SaveChangesAsync(ct); return task.Id;
		}
		var ownId = await SeedSeries("own", "Own recurring work");
		var foreignId = await SeedSeries("foreign", "Foreign recurring work");
		await using var adapter = CreateAdapter(services, "own");
		var chat = Substitute.For<IChatAdapter>();
		chat.DisposeAsync().Returns(ValueTask.CompletedTask);
		await using var planning = new PlanningService(chat, adapter, Substitute.For<ILogger<PlanningService>>());
		await planning.SwitchModeAsync(mode, ct);
		var prompt = chat.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IChatAdapter.ResetChatSession)).GetArguments()[0] as string;
		prompt.Should().Contain("Own recurring work").And.Contain(ownId.ToString()).And.Contain("occurrenceDate")
			.And.Contain("completionActivity").And.Contain("scheduledRange").And.Contain("pendingOccurrenceCount")
			.And.NotContain("Foreign recurring work").And.NotContain(foreignId.ToString());
		prompt.Should().Contain("do not deduplicate occurrences by TaskId").And.Contain("Parent closure adds no completed work");
	}
}

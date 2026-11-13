using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Inbox;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Domain.Users;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;
using OwnPlanner.Infrastructure.Tasks;
using OwnPlanner.Infrastructure.WeeklyReviews;
using OwnPlanner.Mcp.Tools;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed class RecurrenceReconciliationWorkerTests : IAsyncLifetime
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ownplanner-recurrence-host-{Guid.NewGuid():N}");
	private readonly Clock _clock = new(new(2026, 11, 15, 18, 0, 0, DateTimeKind.Utc));
	private ServiceProvider _services = null!;
	private CancellationToken Ct => TestContext.Current.CancellationToken;

	public async ValueTask InitializeAsync()
	{
		Directory.CreateDirectory(_directory);
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddHttpContextAccessor();
		services.AddSingleton<TimeProvider>(_clock);
		services.AddDbContext<AuthDbContext>(options => options.UseSqlite($"Data Source={Path.Combine(_directory, "auth.db")};Pooling=False"));
		services.AddSingleton<IPlannerSessionContextAccessor, PlannerSessionContextAccessor>();
		services.AddScoped<IPlannerDbContextFactory>(sp => new PlannerAppDbContextFactory(_directory,
			sp.GetRequiredService<IPlannerSessionContextAccessor>(), sp.GetRequiredService<IHttpContextAccessor>(),
			sp.GetRequiredService<ILogger<PlannerAppDbContextFactory>>()));
		services.AddSingleton<IPerUserAppInitializationService, PerUserAppInitializationService>();
		services.AddScoped<ITaskListRepository, TaskListRepository>();
		services.AddScoped<INoteListRepository, NoteListRepository>();
		services.AddScoped<IInboxSeeder, InboxSeeder>();
		services.AddScoped<IWeeklyReviewStore, WeeklyReviewStore>();
		services.AddScoped<IPlanningCalendar, PlanningCalendar>();
		services.AddScoped<IRecurringTaskStore, RecurringTaskStore>();
		services.AddScoped<IRecurrenceCalculator, RecurrenceCalculator>();
		services.AddScoped<IRecurringTaskLifecycle, RecurringTaskLifecycle>();
		services.AddScoped<IRecurringTaskSuccessors, RecurringTaskSuccessors>();
		services.AddSingleton<RecurrenceReconciliationWorker>();
		_services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
		using var scope = _services.CreateScope();
		await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.MigrateAsync(Ct);
	}
	public async ValueTask DisposeAsync()
	{
		await _services.DisposeAsync();
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
		Directory.Delete(_directory, true);
	}

	private async Task<User> Account(string name, bool active = true)
	{
		var user = new User($"{name}@example.com", name, "test hash");
		if (!active) user.Deactivate();
		using var scope = _services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
		db.Add(user); await db.SaveChangesAsync(Ct); return user;
	}
	private async Task<TaskItem> Seed(User user, string zone)
	{
		var context = new SessionContext { UserId = user.Id.ToString(), SessionId = "test" };
		await _services.GetRequiredService<IPerUserAppInitializationService>().EnsureInitializedAsync(context, Ct);
		using var session = _services.GetRequiredService<IPlannerSessionContextAccessor>().BeginScope(context);
		using var scope = _services.CreateScope();
		var factory = scope.ServiceProvider.GetRequiredService<IPlannerDbContextFactory>();
		await using var db = await factory.CreateAsync(Ct);
		var list = new TaskList("Private list");
		var task = new TaskItem("Private series", list.Id);
		task.ConfigureRecurrence(new(RecurrenceRule.Weekly(1, [DayOfWeek.Friday], DayOfWeek.Monday),
			new(2026, 11, 13), new(2026, 11, 13), MissedOccurrencePolicy.SkipExpiredPeriods));
		db.AddRange(list, task); await db.SaveChangesAsync(Ct);
		await new WeeklyReviewService(scope.ServiceProvider.GetRequiredService<IWeeklyReviewStore>(), _clock)
			.ConfigureAsync(false, zone, 1, "18:00", ct: Ct);
		return task;
	}
	private async Task<(TaskItem Task, TaskOccurrenceFactRow[] Facts)> Read(User user)
	{
		using var session = _services.GetRequiredService<IPlannerSessionContextAccessor>().BeginScope(new() { UserId = user.Id.ToString(), SessionId = "test-read" });
		using var scope = _services.CreateScope();
		await using var db = await scope.ServiceProvider.GetRequiredService<IPlannerDbContextFactory>().CreateAsync(Ct);
		return (await db.TaskItems.SingleAsync(Ct), await db.TaskOccurrenceFacts.ToArrayAsync(Ct));
	}

	[Fact]
	public async Task IndependentTicksCatchUpEveryActiveTenantUsingOwnPreferencesWithNoReminderLinks()
	{
		var a = await Account("tokyo"); var b = await Account("losangeles"); var inactive = await Account("inactive", false);
		var taskA = await Seed(a, "Asia/Tokyo"); var taskB = await Seed(b, "America/Los_Angeles");
		var worker = _services.GetRequiredService<RecurrenceReconciliationWorker>();
		await worker.RunOnceAsync(Ct);
		var firstA = await Read(a); var firstB = await Read(b);
		firstA.Task.IsCompleted.Should().BeTrue(); firstA.Facts.Should().ContainSingle().Which.TaskId.Should().Be(taskA.Id);
		firstB.Task.IsCompleted.Should().BeFalse(); firstB.Facts.Should().BeEmpty();
		File.Exists(Path.Combine(_directory, $"ownplanner-user-{inactive.Id}.db")).Should().BeFalse();
		_services.GetRequiredService<IPlannerSessionContextAccessor>().Current.Should().BeNull();
		// A later tick uses the same once-per-process initializer, but reconciliation must run again.
		_clock.Now = _clock.Now.AddDays(1);
		await worker.RunOnceAsync(Ct); await worker.RunOnceAsync(Ct);
		var secondB = await Read(b);
		secondB.Task.IsCompleted.Should().BeTrue(); secondB.Facts.Should().ContainSingle().Which.TaskId.Should().Be(taskB.Id);
		(await Read(a)).Facts.Should().ContainSingle();
		using var scope = _services.CreateScope();
		(await scope.ServiceProvider.GetRequiredService<AuthDbContext>().TelegramAccountLinks.CountAsync(Ct)).Should().Be(0);
	}

	[Fact]
	public async Task UnauthenticatedLifecycleFailsAndForeignOccurrenceCannotBeReadOrMutated()
	{
		using (var scope = _services.CreateScope())
		{
			var action = () => scope.ServiceProvider.GetRequiredService<IRecurringTaskLifecycle>().ReconcileAsync(Ct);
			await action.Should().ThrowAsync<UnauthorizedAccessException>();
		}
		var a = await Account("owner"); var b = await Account("other");
		var owned = await Seed(a, "UTC"); await Seed(b, "UTC");
		using (var session = _services.GetRequiredService<IPlannerSessionContextAccessor>().BeginScope(new() { UserId = b.Id.ToString(), SessionId = "test-isolation" }))
		using (var scope = _services.CreateScope())
		{
			var service = scope.ServiceProvider.GetRequiredService<IRecurringTaskLifecycle>();
			var complete = () => service.CompleteOccurrenceAsync(new(owned.Id, new(2026, 11, 13)), ct: Ct);
			await complete.Should().ThrowAsync<KeyNotFoundException>();
			var cancel = () => service.CancelOccurrenceAsync(new(owned.Id, new(2026, 11, 13)), Ct);
			await cancel.Should().ThrowAsync<KeyNotFoundException>();
			var end = () => service.EndSeriesAsync(owned.Id, Ct);
			await end.Should().ThrowAsync<KeyNotFoundException>();
			var trash = () => service.TrashSeriesAsync(owned.Id, Ct);
			await trash.Should().ThrowAsync<KeyNotFoundException>();
			var tx = () => scope.ServiceProvider.GetRequiredService<IRecurringTaskStore>().ExecuteAsync(owned.Id, (_, _) => Task.FromResult(true), Ct);
			await tx.Should().ThrowAsync<KeyNotFoundException>();
		}
		(await Read(a)).Facts.Should().BeEmpty(); (await Read(b)).Facts.Should().BeEmpty();
	}

	[Fact]
	public async Task SuccessorPreviewAndApplyUseOnlyTheAuthenticatedTenantAndIsolateRequestKeys()
	{
		var request = new RecurringSuccessorRequest(RecurringSuccessorKind.Replace, new(2026, 11, 16), ClearEndDate: true);
		using (var scope = _services.CreateScope())
		{
			var service = scope.ServiceProvider.GetRequiredService<IRecurringTaskSuccessors>();
			var preview = () => service.PreviewAsync(Guid.NewGuid(), request, Ct);
			await preview.Should().ThrowAsync<UnauthorizedAccessException>();
			var apply = () => service.ApplyAsync(Guid.NewGuid(), Guid.NewGuid(), request, "token", ct: Ct);
			await apply.Should().ThrowAsync<UnauthorizedAccessException>();
		}
		var a = await Account("successor-owner"); var b = await Account("successor-other");
		var taskA = await Seed(a, "UTC"); var taskB = await Seed(b, "UTC"); var key = Guid.NewGuid();
		async Task<RecurringSuccessorResult> OwnSuccessor(User user, TaskItem task, Guid foreignId)
		{
			using var session = _services.GetRequiredService<IPlannerSessionContextAccessor>().BeginScope(new() { UserId = user.Id.ToString(), SessionId = "successor-test" });
			using var scope = _services.CreateScope();
			var service = scope.ServiceProvider.GetRequiredService<IRecurringTaskSuccessors>();
			var foreignPreview = () => service.PreviewAsync(foreignId, request, Ct);
			await foreignPreview.Should().ThrowAsync<KeyNotFoundException>();
			var preview = await service.PreviewAsync(task.Id, request, Ct);
			var foreignApply = () => service.ApplyAsync(foreignId, key, request, preview.PreviewToken, ct: Ct);
			await foreignApply.Should().ThrowAsync<KeyNotFoundException>();
			return await service.ApplyAsync(task.Id, key, request, preview.PreviewToken, ct: Ct);
		}
		var resultA = await OwnSuccessor(a, taskA, taskB.Id); var resultB = await OwnSuccessor(b, taskB, taskA.Id);
		resultA.SuccessorTaskId.Should().NotBe(resultB.SuccessorTaskId);
		foreach (var (user, result, foreign) in new[] { (a, resultA, resultB), (b, resultB, resultA) })
		{
			using var session = _services.GetRequiredService<IPlannerSessionContextAccessor>().BeginScope(new() { UserId = user.Id.ToString(), SessionId = "verify-successor" });
			using var scope = _services.CreateScope();
			await using var db = await scope.ServiceProvider.GetRequiredService<IPlannerDbContextFactory>().CreateAsync(Ct);
			(await db.TaskItems.CountAsync(Ct)).Should().Be(2); (await db.TaskOccurrenceFacts.CountAsync(Ct)).Should().Be(0);
			(await db.TaskItems.AnyAsync(t => t.Id == foreign.SuccessorTaskId, Ct)).Should().BeFalse();
			var operation = await db.RecurringSuccessorOperations.SingleAsync(Ct);
			operation.RequestId.Should().Be(key); operation.SuccessorTaskId.Should().Be(result.SuccessorTaskId);
		}
	}

	[Fact]
	public async Task OneBrokenDatabaseDoesNotPreventOtherAccountsCatchingUp()
	{
		var broken = await Account("broken"); var healthy = await Account("healthy");
		await File.WriteAllTextAsync(Path.Combine(_directory, $"ownplanner-user-{broken.Id}.db"), "invalid sqlite", Ct);
		await Seed(healthy, "Asia/Tokyo");
		await _services.GetRequiredService<RecurrenceReconciliationWorker>().RunOnceAsync(Ct);
		(await Read(healthy)).Facts.Should().ContainSingle();
		_services.GetRequiredService<IPlannerSessionContextAccessor>().Current.Should().BeNull();
	}

	private sealed class Clock(DateTime now) : TimeProvider
	{
		public DateTime Now { get; set; } = now;
		public override DateTimeOffset GetUtcNow() => new(Now);
	}
}

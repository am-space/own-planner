using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Planner;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Planner;

namespace OwnPlanner.Infrastructure.Tests.Planner;

public partial class PlannerReadStoreTests
{
	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public async Task TaskReads_AllowConcurrentWalCommit_WhileKeepingTheOriginalSnapshot(bool detail, bool recurring)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(20)); var ct = timeout.Token;
		var directory = Path.Combine(Path.GetTempPath(), $"ownplanner-planner-snapshot-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		var gate = new PauseBeforeSecondRead();
		Task<(int Count, string Title, PlannerRecurrenceDto? Recurrence)>? pendingRead = null;
		try
		{
			var path = Path.Combine(directory, "planner.db");
			var writerFactory = new ConcurrentPlannerFactory(path);
			Guid taskId, listId, previousId = default;
			await using (var db = await writerFactory.CreateAsync(ct))
			{
				await db.Database.EnsureCreatedAsync(ct);
				await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
				var list = new TaskList("Snapshot list"); listId = list.Id;
				var task = new TaskItem("Target", list.Id);
				if (recurring)
				{
					var previous = new TaskItem("Earlier source", list.Id); previousId = previous.Id;
					previous.ConfigureRecurrence(new(RecurrenceRule.Weekly(1, [DayOfWeek.Friday], DayOfWeek.Monday),
						new(2026, 11, 13), null, MissedOccurrencePolicy.RetainEachObligation));
					task = previous.CreateRecurrenceSuccessor(new(RecurrenceRule.Monthly(1, [31]),
						new(2026, 11, 16), null, MissedOccurrencePolicy.RetainEachObligation));
					task.SetTitle("Target"); db.Add(previous);
				}
				taskId = task.Id; db.AddRange(list, task); await db.SaveChangesAsync(ct);
			}
			var calendar = Substitute.For<IPlanningCalendar>();
			calendar.ResolveAsync("today", Arg.Any<CancellationToken>()).Returns(PlanningCalendarResolver.Resolve("today",
				new(2026, 11, 15, 18, 0, 0, DateTimeKind.Utc), "UTC"));
			var store = new PlannerReadStore(new ConcurrentPlannerFactory(path, gate), calendar, new RecurrenceCalculator());
			async Task<(int Count, string Title, PlannerRecurrenceDto? Recurrence)> ReadAsync()
			{
				if (detail)
				{
					var item = (await store.GetTaskAsync(taskId, ct))!;
					return (1, item.Title, item.Recurrence);
				}
				var page = await store.QueryTasksAsync(new(Search: "Target"), ct);
				var selected = page.Items.Single(item => item.Id == taskId);
				return (page.TotalCount, selected.Title, selected.Recurrence);
			}
			pendingRead = ReadAsync();
			await gate.Paused.Task.WaitAsync(ct); // count/detail established the reader's snapshot
			await using (var writer = await writerFactory.CreateAsync(ct))
			{
				var task = await writer.TaskItems.SingleAsync(t => t.Id == taskId, ct);
				task.SetTitle("Target changed");
				if (recurring)
					writer.TaskOccurrenceFacts.Add(new() { TaskId = previousId, OccurrenceDate = new(2026, 11, 13), Status = TaskOccurrenceStatus.Cancelled });
				else task.ConfigureRecurrence(new(RecurrenceRule.Weekly(1, [DayOfWeek.Friday], DayOfWeek.Monday),
					new(2026, 11, 13), null, MissedOccurrencePolicy.RetainEachObligation));
				writer.Add(new TaskItem("Target inserted", listId));
				await writer.SaveChangesAsync(ct); // must commit before the reader is released
			}
			pendingRead.IsCompleted.Should().BeFalse();
			gate.Resume.TrySetResult();
			var original = await pendingRead;
			original.Count.Should().Be(1); original.Title.Should().Be("Target");
			if (recurring) original.Recurrence!.PreviousSeriesHasOutstandingWork.Should().BeTrue();
			else original.Recurrence.Should().BeNull();
			var fresh = await ReadAsync();
			fresh.Count.Should().Be(detail ? 1 : 2); fresh.Title.Should().Be("Target changed");
			fresh.Recurrence.Should().NotBeNull(); fresh.Recurrence!.PreviousSeriesHasOutstandingWork.Should().BeFalse();
		}
		finally
		{
			gate.Resume.TrySetResult();
			try { if (pendingRead is not null) await pendingRead; }
			finally { Directory.Delete(directory, true); }
		}
	}

	private sealed class PauseBeforeSecondRead : DbCommandInterceptor
	{
		private int _reads;
		public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
			CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
		{
			if (Interlocked.Increment(ref _reads) == 2)
			{
				Paused.TrySetResult(); await Resume.Task.WaitAsync(cancellationToken);
			}
			return result;
		}
	}
	private sealed class ConcurrentPlannerFactory(string path, IInterceptor? interceptor = null) : IPlannerDbContextFactory
	{
		public ValueTask<AppDbContext> CreateAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(
			new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
				.UseSqlite($"Data Source={path};Pooling=False;Default Timeout=1")
				.AddInterceptors(interceptor is null ? [] : [interceptor]).Options));
		public Task DeleteUserDatabaseAsync(string userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}

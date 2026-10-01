using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;

namespace OwnPlanner.Infrastructure.Tests.Tasks;

public sealed class TaskGoalLinkRepositoryTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"ownplanner-link-{Guid.NewGuid():N}.db");
	private IPlannerDbContextFactory Factory => new FileFactory(_path);
	private TaskItemRepository Repository => new(Factory);

	private async Task<(TaskItem Task, TaskList List, Goal Goal)> Seed(CancellationToken ct)
	{
		await using var db = await Factory.CreateAsync(ct);
		await db.Database.EnsureCreatedAsync(ct);
		var list = new TaskList("Training");
		var goal = new Goal("Run a half marathon", GoalHorizon.Quarterly, targetPeriod: "2026-Q4");
		var task = new TaskItem("Book physiotherapy", list.Id, "Knee assessment", DateTime.UtcNow.AddDays(2), true);
		task.SetFocusAt(DateTime.UtcNow.AddDays(1));
		db.AddRange(list, goal, task); await db.SaveChangesAsync(ct);
		return (task, list, goal);
	}

	[Fact]
	public async Task ChangesOnlyGoalAndUpdatedAtAndRepeatedCallDoesNotTouchTask()
	{
		var ct = TestContext.Current.CancellationToken;
		var (task, _, goal) = await Seed(ct);
		var linked = await Repository.LinkToActiveGoalAsync(task.Id, goal.Id, ct);
		linked.Status.Should().Be(TaskGoalLinkStatus.Linked);
		linked.Task.Should().BeEquivalentTo(task, o => o.Excluding(t => t.GoalId).Excluding(t => t.UpdatedAt));
		linked.Task!.GoalId.Should().Be(goal.Id);
		linked.Task.UpdatedAt.Should().BeAfter(task.UpdatedAt);
		var repeated = await Repository.LinkToActiveGoalAsync(task.Id, goal.Id, ct);
		repeated.Status.Should().Be(TaskGoalLinkStatus.AlreadyLinked);
		repeated.Task.Should().BeEquivalentTo(linked.Task);
		task.SetImportant(false);
		task.UpdatedAt.Should().BeAfter(linked.Task.UpdatedAt);
	}

	[Theory]
	[InlineData("trash", TaskGoalLinkStatus.TaskNotFound)]
	[InlineData("missingTask", TaskGoalLinkStatus.TaskNotFound)]
	[InlineData("archive", TaskGoalLinkStatus.TaskListUnavailable)]
	[InlineData("missingList", TaskGoalLinkStatus.TaskNotFound)]
	[InlineData("pause", TaskGoalLinkStatus.GoalInactive)]
	[InlineData("achieve", TaskGoalLinkStatus.GoalInactive)]
	[InlineData("drop", TaskGoalLinkStatus.GoalInactive)]
	[InlineData("missingGoal", TaskGoalLinkStatus.GoalNotFound)]
	[InlineData("conflict", TaskGoalLinkStatus.ConflictingLink)]
	public async Task RechecksCurrentEligibilityAfterAnotherConnectionChangesSuggestionSnapshot(string change, TaskGoalLinkStatus expected)
	{
		var ct = TestContext.Current.CancellationToken;
		var (task, list, goal) = await Seed(ct);
		// The caller has a valid but detached suggestion snapshot. A separate connection changes it.
		var snapshot = await Repository.GetAsync(task.Id, ct);
		snapshot!.GoalId.Should().BeNull();
		await using (var db = await Factory.CreateAsync(ct))
		{
			switch (change)
			{
				case "trash": task.Trash(); db.Update(task); break;
				case "missingTask": db.Remove(task); break;
				case "archive": list.Archive(); db.Update(list); break;
				case "missingList": db.Remove(list); break;
				case "pause": goal.SetStatus(GoalStatus.Paused); db.Update(goal); break;
				case "achieve": goal.SetStatus(GoalStatus.Achieved); db.Update(goal); break;
				case "drop": goal.SetStatus(GoalStatus.Dropped); db.Update(goal); break;
				case "missingGoal": db.Remove(goal); break;
				case "conflict": task.SetGoalId(Guid.NewGuid()); db.Update(task); break;
			}
			await db.SaveChangesAsync(ct);
		}
		var result = await Repository.LinkToActiveGoalAsync(task.Id, goal.Id, ct);
		result.Status.Should().Be(expected); result.Task.Should().BeNull();
		await using var verify = await Factory.CreateAsync(ct);
		var saved = await verify.TaskItems.AsNoTracking().SingleOrDefaultAsync(t => t.Id == task.Id, ct);
		if (saved is not null)
		{
			saved.GoalId.Should().Be(task.GoalId);
			saved.TrashedAt.Should().Be(task.TrashedAt);
			saved.UpdatedAt.Should().Be(task.UpdatedAt);
		}
	}

	[Fact]
	public async Task ConcurrentDifferentLinksHaveOneWinnerAndNeverOverwriteIt()
	{
		var ct = TestContext.Current.CancellationToken;
		var (task, _, goal) = await Seed(ct);
		var other = new Goal("Improve mobility", GoalHorizon.Yearly);
		await using (var db = await Factory.CreateAsync(ct)) { db.Add(other); await db.SaveChangesAsync(ct); }
		var results = await Task.WhenAll(
			Task.Run(() => Repository.LinkToActiveGoalAsync(task.Id, goal.Id, ct), ct),
			Task.Run(() => Repository.LinkToActiveGoalAsync(task.Id, other.Id, ct), ct));
		results.Select(r => r.Status).Should().BeEquivalentTo(new[] { TaskGoalLinkStatus.Linked, TaskGoalLinkStatus.ConflictingLink });
		var winner = results.Single(r => r.Status == TaskGoalLinkStatus.Linked).Task!;
		var saved = await Repository.GetAsync(task.Id, ct);
		saved!.GoalId.Should().Be(winner.GoalId); saved.UpdatedAt.Should().Be(winner.UpdatedAt);
	}

	private sealed class FileFactory(string path) : IPlannerDbContextFactory
	{
		public ValueTask<AppDbContext> CreateAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new AppDbContext(
			new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options));
		public Task DeleteUserDatabaseAsync(string userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
	public void Dispose()
	{
		foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(_path + suffix)) File.Delete(_path + suffix);
	}
}

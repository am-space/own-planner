using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;

namespace OwnPlanner.Infrastructure.Tests.Goals;

public class GoalRepositoryTests
{
	private static AppDbContext CreateDb(out SqliteConnection conn)
	{
		conn = new SqliteConnection("DataSource=:memory:");
		conn.Open();
		var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
		var db = new AppDbContext(options);
		db.Database.EnsureCreated();
		return db;
	}

	[Fact]
	public async Task Add_Get_Update_Delete_Roundtrip()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);

		var repo = new GoalRepository(dbContextFactory);
		var goal = new Goal("Q2 Launch", GoalHorizon.Quarterly, "Ship the product", "2025-Q2", null, "Release v1.0");
		await repo.AddAsync(goal, ct);

		var loaded = await repo.GetAsync(goal.Id, ct);
		loaded!.Title.Should().Be("Q2 Launch");
		loaded.Description.Should().Be("Ship the product");
		loaded.Horizon.Should().Be(GoalHorizon.Quarterly);
		loaded.TargetPeriod.Should().Be("2025-Q2");
		loaded.TargetDate.Should().BeNull();
		loaded.Status.Should().Be(GoalStatus.Active);
		loaded.Metric.Should().Be("Release v1.0");
		loaded.MetricCurrent.Should().BeNull();

		loaded.SetStatus(GoalStatus.Achieved);
		loaded.SetMetricCurrent("Done");
		await repo.UpdateAsync(loaded, ct);

		var updated = await repo.GetAsync(goal.Id, ct);
		updated!.Status.Should().Be(GoalStatus.Achieved);
		updated.MetricCurrent.Should().Be("Done");

		await repo.DeleteAsync(loaded, ct);
		(await repo.GetAsync(goal.Id, ct)).Should().BeNull();
	}

	[Fact]
	public async Task Add_Get_WithTargetDateHorizon()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);

		var repo = new GoalRepository(dbContextFactory);
		var deadline = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);
		var goal = new Goal("Finish book", GoalHorizon.TargetDate, targetDate: deadline);
		await repo.AddAsync(goal, ct);

		var loaded = await repo.GetAsync(goal.Id, ct);
		loaded!.Horizon.Should().Be(GoalHorizon.TargetDate);
		loaded.TargetPeriod.Should().BeNull();
		loaded.TargetDate.Should().NotBeNull();
		loaded.TargetDate!.Value.Date.Should().Be(deadline.Date);
	}

	[Fact]
	public async Task ListAsync_Filters_Inactive_And_Ordering()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);

		var repo = new GoalRepository(dbContextFactory);

		var active = new Goal("Health", GoalHorizon.Quarterly, targetPeriod: "2025-Q2");
		var achieved = new Goal("Learn Spanish", GoalHorizon.Yearly, targetPeriod: "2024");
		var dropped = new Goal("Old Goal", GoalHorizon.Monthly, targetPeriod: "2025-01");
		achieved.SetStatus(GoalStatus.Achieved);
		dropped.SetStatus(GoalStatus.Dropped);

		await repo.AddAsync(active, ct);
		await repo.AddAsync(achieved, ct);
		await repo.AddAsync(dropped, ct);

		var activeOnly = await repo.ListAsync(false, ct);
		activeOnly.Should().HaveCount(1);
		activeOnly.Should().OnlyContain(g => g.Status == GoalStatus.Active);

		var all = await repo.ListAsync(true, ct);
		all.Should().HaveCount(3);
		all.Select(g => g.Status).Should().Contain([GoalStatus.Active, GoalStatus.Achieved, GoalStatus.Dropped]);

		// UpdatedAt ordering desc
		active.SetDescription("Updated");
		await repo.UpdateAsync(active, ct);
		var ordered = await repo.ListAsync(true, ct);
		ordered.First().Id.Should().Be(active.Id);
	}
}

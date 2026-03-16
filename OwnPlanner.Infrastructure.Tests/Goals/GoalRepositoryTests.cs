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

		var repo = new GoalRepository(db);
		var goal = new Goal("Q2 Launch", GoalHorizon.Quarterly, "Ship the product", "2025-Q2", null, "Release v1.0");
		await repo.AddAsync(goal);

		var loaded = await repo.GetAsync(goal.Id);
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
		await repo.UpdateAsync(loaded);

		var updated = await repo.GetAsync(goal.Id);
		updated!.Status.Should().Be(GoalStatus.Achieved);
		updated.MetricCurrent.Should().Be("Done");

		await repo.DeleteAsync(loaded);
		(await repo.GetAsync(goal.Id)).Should().BeNull();
	}

	[Fact]
	public async Task Add_Get_WithTargetDateHorizon()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;

		var repo = new GoalRepository(db);
		var deadline = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);
		var goal = new Goal("Finish book", GoalHorizon.TargetDate, targetDate: deadline);
		await repo.AddAsync(goal);

		var loaded = await repo.GetAsync(goal.Id);
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

		var repo = new GoalRepository(db);

		var active = new Goal("Health", GoalHorizon.Quarterly, targetPeriod: "2025-Q2");
		var achieved = new Goal("Learn Spanish", GoalHorizon.Yearly, targetPeriod: "2024");
		var dropped = new Goal("Old Goal", GoalHorizon.Monthly, targetPeriod: "2025-01");
		achieved.SetStatus(GoalStatus.Achieved);
		dropped.SetStatus(GoalStatus.Dropped);

		await repo.AddAsync(active);
		await repo.AddAsync(achieved);
		await repo.AddAsync(dropped);

		var activeOnly = await repo.ListAsync(false);
		activeOnly.Should().HaveCount(1);
		activeOnly.Should().OnlyContain(g => g.Status == GoalStatus.Active);

		var all = await repo.ListAsync(true);
		all.Should().HaveCount(3);
		all.Select(g => g.Status).Should().Contain([GoalStatus.Active, GoalStatus.Achieved, GoalStatus.Dropped]);

		// UpdatedAt ordering desc
		active.SetDescription("Updated");
		await repo.UpdateAsync(active);
		var ordered = await repo.ListAsync(true);
		ordered.First().Id.Should().Be(active.Id);
	}
}

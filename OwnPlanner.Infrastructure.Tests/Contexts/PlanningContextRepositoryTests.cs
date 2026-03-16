using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;

namespace OwnPlanner.Infrastructure.Tests.Contexts;

public class PlanningContextRepositoryTests
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

		var repo = new PlanningContextRepository(db);
		var context = new PlanningContext("Health", ContextType.Area, "Physical wellbeing", "#4CAF50");
		await repo.AddAsync(context);

		var loaded = await repo.GetAsync(context.Id);
		loaded!.Name.Should().Be("Health");
		loaded.Type.Should().Be(ContextType.Area);
		loaded.Description.Should().Be("Physical wellbeing");
		loaded.Color.Should().Be("#4CAF50");
		loaded.Status.Should().Be(ContextStatus.Active);

		loaded.SetName("Health & Fitness");
		loaded.SetStatus(ContextStatus.Paused);
		await repo.UpdateAsync(loaded);

		var updated = await repo.GetAsync(context.Id);
		updated!.Name.Should().Be("Health & Fitness");
		updated.Status.Should().Be(ContextStatus.Paused);

		await repo.DeleteAsync(loaded);
		(await repo.GetAsync(context.Id)).Should().BeNull();
	}

	[Fact]
	public async Task ListAsync_Filters_Archived_And_Ordering()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;

		var repo = new PlanningContextRepository(db);

		var active = new PlanningContext("Work", ContextType.Project);
		var paused = new PlanningContext("Health", ContextType.Area);
		var completed = new PlanningContext("Q1 Launch", ContextType.Project);
		var archived = new PlanningContext("Old Project", ContextType.Project);
		paused.SetStatus(ContextStatus.Paused);
		completed.SetStatus(ContextStatus.Completed);
		archived.SetStatus(ContextStatus.Archived);

		await repo.AddAsync(active);
		await repo.AddAsync(paused);
		await repo.AddAsync(completed);
		await repo.AddAsync(archived);

		// Only Archived is excluded by default
		var visible = await repo.ListAsync(false);
		visible.Should().HaveCount(3);
		visible.Should().NotContain(c => c.Status == ContextStatus.Archived);
		visible.Select(c => c.Status).Should().Contain([ContextStatus.Active, ContextStatus.Paused, ContextStatus.Completed]);

		var all = await repo.ListAsync(true);
		all.Should().HaveCount(4);

		// UpdatedAt ordering desc
		active.SetDescription("Updated");
		await repo.UpdateAsync(active);
		var ordered = await repo.ListAsync(true);
		ordered.First().Id.Should().Be(active.Id);
	}
}

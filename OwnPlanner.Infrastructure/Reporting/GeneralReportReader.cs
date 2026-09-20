using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Domain;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Reporting;

public sealed class GeneralReportReader(IPlannerDbContextFactory dbContextFactory, TimeProvider timeProvider) : IGeneralReportReader
{
	public async Task<GeneralReport> GetAsync(CancellationToken cancellationToken = default)
	{
		var asOfUtc = timeProvider.GetUtcNow().UtcDateTime;
		await using var db = await dbContextFactory.CreateAsync(cancellationToken);
		await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
		var tasks = await db.TaskItems.AsNoTracking()
			.Where(t => t.TrashedAt == null && !db.TaskLists.Any(l => l.Id == t.TaskListId && l.IsArchived))
			.Select(t => new GeneralTaskRow(t.Id, t.Title, t.IsCompleted, t.FocusAt, t.DueAt, t.TaskListId, t.GoalId))
			.ToListAsync(cancellationToken);
		var goals = await db.Goals.AsNoTracking().Where(g => g.Status == GoalStatus.Active)
			.Select(g => new GeneralGoalRow(g.Id, g.Title)).ToListAsync(cancellationToken);
		var captures = await db.NoteItems.AsNoTracking()
			.CountAsync(n => n.NoteListId == WellKnownIds.InboxNoteList &&
				db.NoteLists.Any(l => l.Id == n.NoteListId && !l.IsArchived), cancellationToken);
		return GeneralReportBuilder.Build(asOfUtc, tasks, goals, captures);
	}
}

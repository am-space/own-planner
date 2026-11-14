using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Domain;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Reporting;

public sealed class GeneralReportReader(IPlannerDbContextFactory dbContextFactory, TimeProvider timeProvider, IPlanningCalendar calendar) : IGeneralReportReader
{
	public Task<GeneralReport> GetAsync(CancellationToken cancellationToken = default) => BuildAsync(null, cancellationToken);

	public async Task<GeneralReport> GetCalendarAsync(string period, CancellationToken cancellationToken = default) =>
		await BuildAsync(await calendar.ResolveAsync(period, cancellationToken), cancellationToken);

	public async Task<GeneralAttentionReport> GetAttentionAsync(GeneralAttentionOptions options, CancellationToken cancellationToken = default)
	{
		options.Validate();
		var period = await calendar.ResolveAsync("thisWeek", cancellationToken);
		await using var db = await dbContextFactory.CreateAsync(cancellationToken);
		await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
		var work = await ReadWorkAsync(db, period, options.Limit, cancellationToken);
		return GeneralAttentionBuilder.Build(period, await ReadTasksAsync(db, cancellationToken), options, work);
	}

	public async Task<DayReport> GetDayAsync(DayReportOptions options, CancellationToken cancellationToken = default)
	{
		options.Validate();
		var period = await calendar.ResolveAsync("today", cancellationToken);
		await using var db = await dbContextFactory.CreateAsync(cancellationToken);
		await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
		var work = await ReadWorkAsync(db, period, options.Limit, cancellationToken);
		return DayReportBuilder.Build(period, await ReadTasksAsync(db, cancellationToken), options, work);
	}

	private static Task<List<GeneralTaskRow>> ReadTasksAsync(AppDbContext db, CancellationToken cancellationToken) =>
		db.TaskItems.AsNoTracking()
			.Where(t => t.TrashedAt == null && !db.TaskLists.Any(l => l.Id == t.TaskListId && l.IsArchived))
			.Select(t => new GeneralTaskRow(t.Id, t.Title, t.IsCompleted, t.FocusAt, t.DueAt, t.TaskListId, t.GoalId) { IsImportant = t.IsImportant, IsRecurring = t.Recurrence != null })
			.ToListAsync(cancellationToken);

	private static Task<ReportWork> ReadWorkAsync(AppDbContext db, PlanningPeriod period, int limit, CancellationToken ct) =>
		ReportWorkReader.ReadAsync(db, period, new(period.StartDate, period.EndExclusiveDate), period.StartsAtUtc, period.EndsAtUtc, limit, ct, includeGroups: false);

	private async Task<GeneralReport> BuildAsync(PlanningPeriod? period, CancellationToken cancellationToken)
	{
		var asOfUtc = period?.AsOfUtc ?? timeProvider.GetUtcNow().UtcDateTime;
		await using var db = await dbContextFactory.CreateAsync(cancellationToken);
		await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
		var tasks = await ReadTasksAsync(db, cancellationToken);
		var goals = await db.Goals.AsNoTracking().Where(g => g.Status == GoalStatus.Active)
			.Select(g => new GeneralGoalRow(g.Id, g.Title)).ToListAsync(cancellationToken);
		var captures = await db.NoteItems.AsNoTracking()
			.CountAsync(n => n.NoteListId == WellKnownIds.InboxNoteList &&
				db.NoteLists.Any(l => l.Id == n.NoteListId && !l.IsArchived), cancellationToken);
		var utc = ReportWorkReader.UtcCalendar(asOfUtc);
		var work = await ReportWorkReader.ReadAsync(db, utc, new(utc.Today, utc.Today.AddDays(8)), utc.StartsAtUtc, utc.EndsAtUtc, 5, cancellationToken, includeGroups: false);
		var localWork = period is null ? null : await ReadWorkAsync(db, period, 5, cancellationToken);
		return GeneralReportBuilder.Build(asOfUtc, tasks, goals, captures, work) with
		{
			Calendar = period is null ? null : GeneralReportBuilder.BuildCalendar(period, tasks) with
			{
				Work = localWork,
				Attention = period.Name == "thisWeek" ? GeneralAttentionBuilder.Build(period, tasks, work: localWork) : null
			}
		};
	}
}

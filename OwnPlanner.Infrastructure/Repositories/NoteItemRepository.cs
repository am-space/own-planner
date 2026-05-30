using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class NoteItemRepository(IPlannerDbContextFactory dbContextFactory)
	: PlannerRepositoryBase<NoteItem>(dbContextFactory), INoteItemRepository
{
	public async Task<IReadOnlyList<NoteItem>> ListAsync(CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var items = await db.NoteItems.ToListAsync(ct).ConfigureAwait(false);
		return items
			.OrderByDescending(n => n.IsPinned)
			.ThenByDescending(n => n.UpdatedAt)
			.ToList();
	}

	public async Task<IReadOnlyList<NoteItem>> ListByNoteListAsync(Guid noteListId, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var items = await db.NoteItems.Where(n => n.NoteListId == noteListId).ToListAsync(ct).ConfigureAwait(false);
		return items
			.OrderByDescending(n => n.IsPinned)
			.ThenByDescending(n => n.UpdatedAt)
			.ToList();
	}

	public async Task<IReadOnlyList<NoteItem>> ListByGoalAsync(Guid goalId, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var items = await db.NoteItems.Where(n => n.GoalId == goalId).ToListAsync(ct).ConfigureAwait(false);
		return items
			.OrderByDescending(n => n.IsPinned)
			.ThenByDescending(n => n.UpdatedAt)
			.ToList();
	}
}

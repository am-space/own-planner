using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class NoteItemRepository(AppDbContext db)
	: RepositoryBase<NoteItem, AppDbContext>(db), INoteItemRepository
{
	public async Task<IReadOnlyList<NoteItem>> ListAsync(CancellationToken ct = default)
	{
		var items = await Set.ToListAsync(ct);
		return items
			.OrderByDescending(n => n.IsPinned)
			.ThenByDescending(n => n.UpdatedAt)
			.ToList();
	}

	public async Task<IReadOnlyList<NoteItem>> ListByNoteListAsync(Guid noteListId, CancellationToken ct = default)
	{
		var items = await Set.Where(n => n.NoteListId == noteListId).ToListAsync(ct);
		return items
			.OrderByDescending(n => n.IsPinned)
			.ThenByDescending(n => n.UpdatedAt)
			.ToList();
	}

	public async Task<IReadOnlyList<NoteItem>> ListByGoalAsync(Guid goalId, CancellationToken ct = default)
	{
		var items = await Set.Where(n => n.GoalId == goalId).ToListAsync(ct);
		return items
			.OrderByDescending(n => n.IsPinned)
			.ThenByDescending(n => n.UpdatedAt)
			.ToList();
	}
}

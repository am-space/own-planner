using OwnPlanner.Domain.Contexts;

namespace OwnPlanner.Application.Contexts;

public class PlanningContextService(IPlanningContextRepository repository) : IPlanningContextService
{
	private readonly IPlanningContextRepository _repository = repository;

	public async Task<PlanningContextDto> CreateAsync(string name, ContextType type, string? description = null, string? color = null, CancellationToken ct = default)
	{
		var context = new PlanningContext(name, type, description, color);
		await _repository.AddAsync(context, ct);
		return Map(context);
	}

	public async Task<PlanningContextDto?> GetAsync(Guid id, CancellationToken ct = default)
	{
		var context = await _repository.GetAsync(id, ct);
		return context is null ? null : Map(context);
	}

	public async Task<IReadOnlyList<PlanningContextDto>> ListAsync(bool includeArchived = false, CancellationToken ct = default)
	{
		var contexts = await _repository.ListAsync(includeArchived, ct);
		return contexts.Select(Map).ToList();
	}

	public async Task<PlanningContextDto> UpdateAsync(Guid id, string? name = null, ContextType? type = null, string? description = null, ContextStatus? status = null, string? color = null, CancellationToken ct = default)
	{
		var context = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"PlanningContext {id} not found");

		if (name is not null)
			context.SetName(name);
		if (type is not null)
			context.SetType(type.Value);
		if (description is not null)
			context.SetDescription(description);
		if (status is not null)
			context.SetStatus(status.Value);
		if (color is not null)
			context.SetColor(color);

		await _repository.UpdateAsync(context, ct);
		return Map(context);
	}

	public async Task DeleteAsync(Guid id, CancellationToken ct = default)
	{
		var context = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"PlanningContext {id} not found");
		await _repository.DeleteAsync(context, ct);
	}

	private static PlanningContextDto Map(PlanningContext context) => new(
		context.Id,
		context.Name,
		context.Type,
		context.Description,
		context.Status,
		context.Color,
		context.CreatedAt,
		context.UpdatedAt
	);
}

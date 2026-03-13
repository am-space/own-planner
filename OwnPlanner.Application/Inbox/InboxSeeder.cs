using Microsoft.Extensions.Logging;
using OwnPlanner.Domain;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Inbox;

public class InboxSeeder(
	ITaskListRepository taskListRepository,
	INoteListRepository noteListRepository,
	ILogger<InboxSeeder> logger) : IInboxSeeder
{
	private readonly ITaskListRepository _taskListRepository = taskListRepository;
	private readonly INoteListRepository _noteListRepository = noteListRepository;
	private readonly ILogger<InboxSeeder> _logger = logger;

	public async Task SeedAsync(CancellationToken ct = default)
	{
		await EnsureInboxTaskListAsync(ct);
		await EnsureInboxNoteListAsync(ct);
	}

	private async Task EnsureInboxTaskListAsync(CancellationToken ct)
	{
		var existing = await _taskListRepository.GetAsync(WellKnownIds.InboxTaskList, ct);
		if (existing is null)
		{
			var inbox = TaskList.CreateSystem(WellKnownIds.InboxTaskList, "Inbox");
			await _taskListRepository.AddAsync(inbox, ct);
			_logger.LogInformation("Created Inbox TaskList with id {Id}", WellKnownIds.InboxTaskList);
		}
	}

	private async Task EnsureInboxNoteListAsync(CancellationToken ct)
	{
		var existing = await _noteListRepository.GetAsync(WellKnownIds.InboxNoteList, ct);
		if (existing is null)
		{
			var inbox = NoteList.CreateSystem(WellKnownIds.InboxNoteList, "Inbox");
			await _noteListRepository.AddAsync(inbox, ct);
			_logger.LogInformation("Created Inbox NoteList with id {Id}", WellKnownIds.InboxNoteList);
		}
	}
}

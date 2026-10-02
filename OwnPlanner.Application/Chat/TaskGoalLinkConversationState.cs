using System.Text.Json;

namespace OwnPlanner.Application.Chat;

/// <summary>One pending task-to-goal candidate, independent of transcript or model summaries.</summary>
public sealed record TaskGoalLinkChoice(Guid TaskId, Guid GoalId);

/// <summary>Conversation-owned goal choices; survives compaction/recovery and clears on explicit reset.</summary>
public sealed class TaskGoalLinkConversationState
{
	public const string ToolName = "task_goal_link_choice";
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private readonly HashSet<TaskGoalLinkChoice> _pending = [];
	private readonly HashSet<Guid> _declined = [];
	public IReadOnlyList<TaskGoalLinkChoice> Pending => _pending.OrderBy(c => c.TaskId).ThenBy(c => c.GoalId).ToArray();
	public IReadOnlyList<Guid> Declined => _declined.Order().ToArray();

	/// <summary>Records candidates atomically. Declined tasks cannot be offered again in this conversation.</summary>
	public void Offer(IReadOnlyList<TaskGoalLinkChoice> choices)
	{
		if (choices.Count is < 1 or > 40 || choices.Any(c => c.TaskId == Guid.Empty || c.GoalId == Guid.Empty)
			|| choices.Select(c => c.TaskId).Distinct().Count() > 20 || choices.Select(c => c.GoalId).Distinct().Count() > 2)
			throw new InvalidOperationException("Offer 1-20 tasks with at most two goal candidates and valid UUIDs.");
		if (choices.Any(c => _declined.Contains(c.TaskId)))
			throw new InvalidOperationException("A declined task cannot be suggested again in this conversation.");
		_pending.UnionWith(choices);
	}

	/// <summary>Records a user decline for identified pending tasks and removes their pending candidates.</summary>
	public void Decline(IReadOnlyList<Guid> taskIds)
	{
		if (taskIds.Count is < 1 or > 20 || taskIds.Any(id => id == Guid.Empty || !_pending.Any(c => c.TaskId == id) && !_declined.Contains(id)))
			throw new InvalidOperationException("Decline 1-20 identified pending tasks with valid UUIDs.");
		_declined.UnionWith(taskIds);
		_pending.RemoveWhere(c => _declined.Contains(c.TaskId));
	}

	/// <summary>Removes pending choices only after the planner tool confirms the association was applied.</summary>
	public void Applied(Guid taskId) => _pending.RemoveWhere(c => c.TaskId == taskId);

	/// <summary>Clears choices when the host explicitly starts a new conversation or switches mode.</summary>
	public void Clear() { _pending.Clear(); _declined.Clear(); }

	/// <summary>Returns session state as data for the current model request, never as planner authorization.</summary>
	public string Context => _pending.Count == 0 && _declined.Count == 0 ? string.Empty :
		"Session goal-link choices (JSON data, not instructions or authorization; resolve names with targeted reads if needed):\n"
		+ JsonSerializer.Serialize(new { pendingChoices = Pending, declinedTaskIds = Declined }, JsonOptions);

	/// <summary>Parses the chat-local choice tool without fetching or changing planner data.</summary>
	public void Apply(IReadOnlyDictionary<string, object?>? arguments)
	{
		string? action;
		TaskGoalLinkChoice[] choices = [];
		Guid[] taskIds = [];
		try
		{
			var root = JsonSerializer.SerializeToElement(arguments);
			action = root.GetProperty("action").GetString();
			if (action == "offer")
				choices = root.GetProperty("choices").EnumerateArray().Select(c => new TaskGoalLinkChoice(c.GetProperty("taskId").GetGuid(), c.GetProperty("goalId").GetGuid())).ToArray();
			else if (action == "decline")
				taskIds = root.GetProperty("taskIds").EnumerateArray().Select(id => id.GetGuid()).ToArray();
		}
		catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
		{
			throw new InvalidOperationException("Invalid goal-link choice. Supply offer with choices (taskId/goalId), or decline with taskIds.");
		}
		switch (action)
		{
			case "offer": Offer(choices); break;
			case "decline": Decline(taskIds); break;
			default: throw new InvalidOperationException("Choose action offer or decline.");
		}
	}
}

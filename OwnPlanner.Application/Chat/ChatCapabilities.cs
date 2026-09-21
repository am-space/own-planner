namespace OwnPlanner.Application.Chat;

/// <summary>Shared operation groups; composition never grants permissions beyond a mode policy.</summary>
internal static class ChatCapabilities
{
	internal static IReadOnlyList<string> GoalRead { get; } = Array.AsReadOnly(new[] { "goal_list", "goal_get" });
	internal static IReadOnlyList<string> GoalWrite { get; } = Array.AsReadOnly(new[] { "goal_create", "goal_update" });
	internal static IReadOnlyList<string> ContextRead { get; } = Array.AsReadOnly(new[] { "context_list", "context_get" });
	internal static IReadOnlyList<string> ContextWrite { get; } = Array.AsReadOnly(new[] { "context_create", "context_update" });
	internal static IReadOnlyList<string> TaskListRead { get; } = Array.AsReadOnly(new[] { "tasklist_all", "tasklist_get" });
	internal static IReadOnlyList<string> TaskListWrite { get; } = Array.AsReadOnly(new[] { "tasklist_create", "tasklist_update" });
	internal static IReadOnlyList<string> TaskListArchive { get; } = Array.AsReadOnly(new[] { "tasklist_archive", "tasklist_unarchive" });
	internal static IReadOnlyList<string> NoteListRead { get; } = Array.AsReadOnly(new[] { "notelist_all", "notelist_get" });
	internal static IReadOnlyList<string> NoteListWrite { get; } = Array.AsReadOnly(new[] { "notelist_create", "notelist_update" });
	internal static IReadOnlyList<string> NoteListArchive { get; } = Array.AsReadOnly(new[] { "notelist_archive", "notelist_unarchive" });
	internal static IReadOnlyList<string> NoteRead { get; } = Array.AsReadOnly(new[] { "noteitem_list_items", "noteitem_get" });
	internal static IReadOnlyList<string> NoteWrite { get; } = Array.AsReadOnly(new[] { "noteitem_create", "noteitem_update" });
	internal static IReadOnlyList<string> NoteOrganize { get; } = Array.AsReadOnly(new[] { "noteitem_assign", "noteitem_pin", "noteitem_unpin" });
	internal static IReadOnlyList<string> TaskRead { get; } = Array.AsReadOnly(new[] { "taskitem_list_items", "taskitem_get" });
	internal static IReadOnlyList<string> TaskProgress { get; } = Array.AsReadOnly(new[] { "taskitem_complete", "taskitem_reopen", "taskitem_set_focus_date", "taskitem_set_important" });
	internal static IReadOnlyList<string> TaskEdit { get; } = Array.AsReadOnly(new[] { "taskitem_create", "taskitem_update", "taskitem_assign" });
	internal static IReadOnlyList<string> TaskRecovery { get; } = Array.AsReadOnly(new[] { "taskitem_list_trash", "taskitem_restore" });

	internal static IReadOnlyList<string> Combine(params IReadOnlyList<string>[] groups) =>
		Array.AsReadOnly(groups.SelectMany(group => group).Distinct(StringComparer.Ordinal).ToArray());
}

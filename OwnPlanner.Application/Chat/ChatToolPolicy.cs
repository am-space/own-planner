using System.Collections.Frozen;

namespace OwnPlanner.Application.Chat;

/// <summary>Host-owned mode permissions, distinct from declarations active for one request.</summary>
public sealed record ChatToolPolicy(
	IReadOnlyList<string> BaselineTools,
	IReadOnlyList<string> AllowedTools,
	bool CanWrite,
	IReadOnlyList<string> SkillIds,
	IReadOnlyList<string> BaselineSkillIds)
{
	public static ChatToolPolicy ForMode(ModeConfig config) => new(
		config.InitialTools ?? config.AllowedTools, config.AllowedTools, config.CanWrite,
		config.SkillIds, config.BaselineSkillIds);
}

/// <summary>Mutable capabilities owned exclusively by one user request, never shared across sessions.</summary>
public sealed class ChatSkillRuntime
{
	private readonly IReadOnlySet<string> _allowed;
	private readonly IReadOnlySet<string> _available;
	private readonly IReadOnlySet<string> _skillIds;
	private readonly bool _canWrite;
	private readonly HashSet<string> _active = new(StringComparer.Ordinal);
	private readonly Dictionary<string, ChatSkill> _loaded = new(StringComparer.Ordinal);

	public ChatSkillRuntime(ChatToolPolicy policy, IEnumerable<string> availableTools)
	{
		_allowed = policy.AllowedTools.ToFrozenSet(StringComparer.Ordinal);
		_available = availableTools.ToFrozenSet(StringComparer.Ordinal);
		_skillIds = policy.SkillIds.ToFrozenSet(StringComparer.Ordinal);
		_canWrite = policy.CanWrite;
		_active.UnionWith(policy.BaselineTools.Where(IsPermitted).Where(_available.Contains));
		foreach (var id in policy.BaselineSkillIds)
		{
			// A partially configured host can still discuss and use its installed baseline tools.
			// Do not attach a skill's instructions when its full capability set is unavailable.
			if (ChatSkillRegistry.All.TryGetValue(id, out var skill) && skill.Tools.Any(tool => !_available.Contains(tool)))
				continue;
			Load(id);
		}
	}

	public IReadOnlySet<string> ActiveTools => _active.ToFrozenSet(StringComparer.Ordinal);
	public string Instructions => string.Join("\n\n", _loaded.Values.Select(skill => $"### {skill.Id}\n{skill.Instructions}"));
	public string Catalog => !_active.Contains(ChatSkillRegistry.LoadToolName) ? string.Empty :
		"Skills available via skill_load (active only for the current user request; load again when needed on a later request):\n" +
		string.Join("\n", ChatSkillRegistry.All.Values.Where(IsSkillPermitted).OrderBy(skill => skill.Id)
			.Select(skill => $"- {skill.Id}: {skill.Description}"));

	/// <summary>Loads atomically after validating every referenced capability; never executes planner tools.</summary>
	public void Load(string? id)
	{
		if (id is null || !ChatSkillRegistry.All.TryGetValue(id, out var skill))
			throw new InvalidOperationException("Unknown skill. Use an identifier from the skill catalog.");
		if (!IsSkillPermitted(skill))
			throw new InvalidOperationException("This skill is not permitted in the active mode.");
		if (skill.Tools.Any(tool => !_available.Contains(tool)))
			throw new InvalidOperationException("This skill is unavailable because required planner tools are not configured.");
		_loaded.TryAdd(skill.Id, skill);
		_active.UnionWith(skill.Tools);
	}

	/// <summary>Checks the declarations seen by the model before the current batch, plus mode permissions.</summary>
	public void EnsureCanExecute(string name, IReadOnlySet<string> declaredTools)
	{
		if (!declaredTools.Contains(name) || !_active.Contains(name) || !IsPermitted(name) || !_available.Contains(name))
			throw new InvalidOperationException("Tool is unavailable or not permitted for this model request. Load a permitted skill first and call its tools in the next round.");
	}

	private bool IsPermitted(string name) => _allowed.Contains(name) && (_canWrite || ChatSkillRegistry.ReadTools.Contains(name));
	private bool IsSkillPermitted(ChatSkill skill) => _skillIds.Contains(skill.Id) && skill.Tools.All(IsPermitted);
}

using System.Collections.Concurrent;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.E2E.Tests.Infrastructure;

public sealed class ScriptedChatScenarioRegistry
{
	private readonly ConcurrentDictionary<string, Func<IMcpAdapter?, Task<ChatTurnResult>>> _scenarios = new(StringComparer.Ordinal);

	public string Register(Func<IMcpAdapter?, Task<ChatTurnResult>> scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		var prompt = $"e2e:{Guid.NewGuid():N}";
		if (!_scenarios.TryAdd(prompt, scenario))
		{
			throw new InvalidOperationException("Could not register the E2E chat scenario.");
		}

		return prompt;
	}

	public string RegisterResponse(string response, int? contextLengthTokens = 100) =>
		Register(_ => Task.FromResult(new ChatTurnResult(response, contextLengthTokens)));

	internal Task<ChatTurnResult> ExecuteAsync(string prompt, IMcpAdapter? mcpAdapter)
	{
		if (!_scenarios.TryRemove(prompt, out var scenario))
		{
			throw new InvalidOperationException($"No scripted E2E chat scenario is registered for prompt '{prompt}'.");
		}

		return scenario(mcpAdapter);
	}
}

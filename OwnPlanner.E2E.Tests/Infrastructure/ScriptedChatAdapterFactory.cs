using OwnPlanner.Application.Chat;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.E2E.Tests.Infrastructure;

public sealed class ScriptedChatAdapterFactory(ScriptedChatScenarioRegistry scenarios) : IChatAdapterFactory
{
	public IChatAdapter Create(IMcpAdapter? mcpAdapter) => new ScriptedChatAdapter(scenarios, mcpAdapter);
}

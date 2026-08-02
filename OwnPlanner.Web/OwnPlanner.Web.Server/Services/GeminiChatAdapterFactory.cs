using Microsoft.Extensions.Options;
using OwnPlanner.Application.Chat;
using OwnPlanner.Infrastructure.Adapters;
using OwnPlanner.Web.Server.Configuration;

namespace OwnPlanner.Web.Server.Services;

/// <summary>
/// Creates the production Gemini chat adapter from the configured web chat settings.
/// </summary>
public sealed class GeminiChatAdapterFactory(IOptions<ChatSettings> settings) : IChatAdapterFactory
{
	private readonly GeminiSettings _settings = settings.Value.Gemini;

	/// <inheritdoc />
	public IChatAdapter Create(IMcpAdapter? mcpAdapter) =>
		new ChatServiceAdapter(
			_settings.ApiKey,
			_settings.Model,
			_settings.MaxToolCallRounds,
			mcpAdapter);
}

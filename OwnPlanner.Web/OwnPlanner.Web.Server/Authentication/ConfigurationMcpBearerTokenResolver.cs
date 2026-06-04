using Microsoft.Extensions.Options;
using OwnPlanner.Web.Server.Configuration;

namespace OwnPlanner.Web.Server.Authentication;

internal sealed class ConfigurationMcpBearerTokenResolver : IMcpBearerTokenResolver
{
	private readonly IReadOnlyDictionary<string, string> _tokenToUserId;

	public ConfigurationMcpBearerTokenResolver(IOptions<McpBearerSettings> settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		var tokenToUserId = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var binding in settings.Value.TokenBindings)
		{
			var token = binding.Token?.Trim() ?? string.Empty;
			var userId = binding.UserId?.Trim() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(token))
			{
				continue;
			}

			if (string.IsNullOrWhiteSpace(userId))
			{
				continue;
			}

			tokenToUserId[token] = userId;
		}

		_tokenToUserId = tokenToUserId;
	}

	public bool TryResolveUserId(string token, out string userId)
	{
		var normalizedToken = token.Trim();
		return _tokenToUserId.TryGetValue(normalizedToken, out userId!);
	}
}

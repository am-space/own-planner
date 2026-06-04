using Microsoft.Extensions.Options;

namespace OwnPlanner.Web.Server.Configuration;

/// <summary>
/// Validates MCP bearer token configuration loaded from application settings.
/// </summary>
public sealed class McpBearerSettingsValidator : IValidateOptions<McpBearerSettings>
{
	public ValidateOptionsResult Validate(string? name, McpBearerSettings options)
	{
		ArgumentNullException.ThrowIfNull(options);

		var failures = new List<string>();
		var tokenBindings = options.TokenBindings;
		if (tokenBindings is null)
		{
			failures.Add("McpBearer:TokenBindings must not be null.");
			return ValidateOptionsResult.Fail(failures);
		}

		var seenTokens = new HashSet<string>(StringComparer.Ordinal);
		var seenUserIds = new HashSet<string>(StringComparer.Ordinal);

		for (var index = 0; index < tokenBindings.Count; index++)
		{
			var binding = tokenBindings[index];
			var token = binding.Token?.Trim() ?? string.Empty;
			var userId = binding.UserId?.Trim() ?? string.Empty;

			if (string.IsNullOrWhiteSpace(token))
			{
				failures.Add($"McpBearer:TokenBindings:{index}:Token must not be empty.");
				continue;
			}

			if (!seenTokens.Add(token))
			{
				failures.Add($"McpBearer:TokenBindings:{index}:Token is duplicated.");
			}

			if (string.IsNullOrWhiteSpace(userId))
			{
				failures.Add($"McpBearer:TokenBindings:{index}:UserId must not be empty.");
				continue;
			}

			if (!seenUserIds.Add(userId))
			{
				failures.Add($"McpBearer:TokenBindings:{index}:UserId is duplicated.");
			}
		}

		return failures.Count > 0
			? ValidateOptionsResult.Fail(failures)
			: ValidateOptionsResult.Success;
	}
}

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
		var seenTokens = new HashSet<string>(StringComparer.Ordinal);

		for (var index = 0; index < options.TokenBindings.Count; index++)
		{
			var binding = options.TokenBindings[index];
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
			}
		}

		return failures.Count > 0
			? ValidateOptionsResult.Fail(failures)
			: ValidateOptionsResult.Success;
	}
}

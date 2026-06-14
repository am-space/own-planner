using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace OwnPlanner.Web.Server.Services;

/// <summary>
/// Serializer options for tool results sent back to the model. Trims context-only noise so large
/// list results don't dominate the chat context:
/// <list type="bullet">
/// <item>null fields are omitted (e.g. an unset <c>description</c>, <c>dueAt</c>, or <c>goalId</c>) — note
/// this drops nulls only, not empty strings;</item>
/// <item>the audit timestamps <c>CreatedAt</c>/<c>UpdatedAt</c> are dropped — they are never used by the
/// model (ordering happens server-side). Functional dates such as <c>dueAt</c>/<c>focusAt</c> are kept,
/// in ISO 8601.</item>
/// </list>
/// This only affects what the model sees; the web/UI path serializes DTOs through its own pipeline.
/// </summary>
internal static class ToolResultJson
{
	public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		TypeInfoResolver = new DefaultJsonTypeInfoResolver
		{
			Modifiers = { DropAuditTimestamps }
		}
	};

	private static void DropAuditTimestamps(JsonTypeInfo typeInfo)
	{
		if (typeInfo.Kind != JsonTypeInfoKind.Object)
			return;

		for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
		{
			var name = typeInfo.Properties[i].Name;
			if (string.Equals(name, "createdAt", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(name, "updatedAt", StringComparison.OrdinalIgnoreCase))
			{
				typeInfo.Properties.RemoveAt(i);
			}
		}
	}
}

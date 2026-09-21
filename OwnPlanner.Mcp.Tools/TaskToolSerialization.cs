using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol;
using OwnPlanner.Application.Tasks;

namespace OwnPlanner.Mcp.Tools;

/// <summary>Preserves explicit deadline removal in full task results across MCP transports.</summary>
public static class TaskToolSerialization
{
	/// <summary>SDK defaults with explicit null deadlines for full task DTOs; list payloads remain compact.</summary>
	public static JsonSerializerOptions Options { get; } = CreateOptions();

	/// <summary>Retains DueAt even when null, so reads and updates can confirm that no deadline exists.</summary>
	public static void KeepTaskDeadline(JsonTypeInfo typeInfo)
	{
		if (typeInfo.Type != typeof(TaskItemDto))
			return;

		var deadline = typeInfo.Properties.Single(property => property.Name == "dueAt");
		deadline.ShouldSerialize = (_, _) => true;
	}

	private static JsonSerializerOptions CreateOptions()
	{
		var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
		options.TypeInfoResolver = options.TypeInfoResolver!.WithAddedModifier(KeepTaskDeadline);
		options.MakeReadOnly();
		return options;
	}
}

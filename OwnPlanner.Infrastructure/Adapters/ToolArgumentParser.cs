using System.Globalization;
using System.Text.Json;

namespace OwnPlanner.Infrastructure.Adapters;

internal static class ToolArgumentParser
{
	internal static string? GetStringArgument(IReadOnlyDictionary<string, object?>? arguments, string argumentName)
	{
		if (arguments == null || !arguments.TryGetValue(argumentName, out var rawValue) || rawValue == null)
		{
			return null;
		}

		if (rawValue is string value)
		{
			return value;
		}

		if (rawValue is JsonElement element)
		{
			return GetStringArgumentFromJsonElement(element, argumentName);
		}

		if (IsSupportedNumericArgument(rawValue))
		{
			return Convert.ToString(rawValue, CultureInfo.InvariantCulture);
		}

		throw new InvalidOperationException($"Tool argument '{argumentName}' must be a string value, but received {rawValue.GetType().Name}.");
	}

	private static string? GetStringArgumentFromJsonElement(JsonElement element, string argumentName)
	{
		return element.ValueKind switch
		{
			JsonValueKind.String => element.GetString(),
			JsonValueKind.Null or JsonValueKind.Undefined => null,
			JsonValueKind.Number => element.ToString(),
			_ => throw new InvalidOperationException($"Tool argument '{argumentName}' must be a string value, but received JSON {element.ValueKind}.")
		};
	}

	private static bool IsSupportedNumericArgument(object value)
	{
		return value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
	}
}

using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Chat;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Web.Server.Services;

/// <summary>
/// Executes OwnPlanner MCP tools directly inside the web server without spawning a separate stdio process.
/// This preserves the MCP-style tool contract that chat relies on while allowing the host to resolve tools
/// from its own DI container and per-user database wiring.
/// </summary>
public sealed class DirectToolMcpAdapter(
	string sessionId,
	string userId,
	IServiceScopeFactory scopeFactory,
	IPlannerSessionContextAccessor sessionContextAccessor,
	PerUserAppInitializationService initializationService,
	ILogger<DirectToolMcpAdapter> logger) : IMcpAdapter
{
	private static readonly NullabilityInfoContext NullabilityContext = new();
	private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);
	private static readonly IReadOnlyDictionary<string, ToolRegistration> ToolRegistrations = BuildToolRegistrations();

	private readonly SessionContext _sessionContext = new()
	{
		SessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId,
		UserId = string.IsNullOrWhiteSpace(userId)
			? throw new UnauthorizedAccessException("Authenticated user id is required for planner tool access.")
			: userId
	};

	public Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		logger.LogDebug("Using direct in-process MCP adapter for session {SessionId} and user {UserId}", _sessionContext.SessionId, _sessionContext.UserId);
		return Task.CompletedTask;
	}


	public Task<IReadOnlyList<McpToolDefinition>> ListToolDetailsAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<McpToolDefinition> toolDefinitions = ToolRegistrations.Values
			.OrderBy(registration => registration.Definition.Name, StringComparer.Ordinal)
			.Select(registration => registration.Definition)
			.ToList();
		return Task.FromResult(toolDefinitions);
	}

	public async Task<string> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments = null, CancellationToken cancellationToken = default)
	{
		if (!ToolRegistrations.TryGetValue(toolName, out var registration))
		{
			throw new KeyNotFoundException($"Tool '{toolName}' is not registered.");
		}

		await initializationService.EnsureInitializedAsync(_sessionContext, cancellationToken).ConfigureAwait(false);

		using var _ = sessionContextAccessor.BeginScope(_sessionContext);
		using var scope = scopeFactory.CreateScope();
		var toolInstance = CreateToolInstance(scope.ServiceProvider, registration.ToolType);
		var parameterValues = BindArguments(registration.Method, arguments, cancellationToken);

		logger.LogDebug("Executing direct tool {ToolName} for session {SessionId} and user {UserId}", toolName, _sessionContext.SessionId, _sessionContext.UserId);

		try
		{
			var invocationTask = registration.Method.Invoke(toolInstance, parameterValues) as Task<object>;
			if (invocationTask is null)
			{
				throw new InvalidOperationException($"Tool '{toolName}' did not return Task<object> as expected.");
			}

			var result = await invocationTask.ConfigureAwait(false);
			return JsonSerializer.Serialize(result, ToolResultJson.Options);
		}
		catch (TargetInvocationException ex) when (ex.InnerException is not null)
		{
			ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
			throw;
		}
	}

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;

	private object CreateToolInstance(IServiceProvider serviceProvider, Type toolType)
	{
		return toolType == typeof(DateTimeTools)
			? ActivatorUtilities.CreateInstance(serviceProvider, toolType, _sessionContext)
			: ActivatorUtilities.CreateInstance(serviceProvider, toolType);
	}

	private static object?[] BindArguments(MethodInfo method, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken)
	{
		var parameters = method.GetParameters();
		var values = new object?[parameters.Length];

		for (var index = 0; index < parameters.Length; index++)
		{
			var parameter = parameters[index];
			if (parameter.ParameterType == typeof(CancellationToken))
			{
				values[index] = cancellationToken;
				continue;
			}
			if (arguments is not null && arguments.TryGetValue(parameter.Name!, out var rawValue))
			{
				values[index] = ConvertArgument(rawValue, parameter);
				continue;
			}

			if (parameter.HasDefaultValue)
			{
				values[index] = parameter.DefaultValue;
				continue;
			}

			throw new InvalidOperationException($"Tool parameter '{parameter.Name}' is required.");
		}

		return values;
	}

	private static object? ConvertArgument(object? rawValue, ParameterInfo parameter)
	{
		var targetType = parameter.ParameterType;
		var parameterName = parameter.Name!;

		if (rawValue is null)
		{
			if (IsNullable(parameter))
			{
				return null;
			}

			throw new InvalidOperationException($"Tool parameter '{parameterName}' cannot be null.");
		}

		var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
		if (underlyingType.IsInstanceOfType(rawValue))
		{
			return rawValue;
		}

		if (rawValue is JsonElement jsonElement)
		{
			if (jsonElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
			{
				if (IsNullable(parameter))
				{
					return null;
				}

				throw new InvalidOperationException($"Tool parameter '{parameterName}' cannot be null.");
			}

			try
			{
				return JsonSerializer.Deserialize(jsonElement.GetRawText(), targetType, JsonSerializerOptions);
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException($"Tool parameter '{parameterName}' could not be converted to {targetType.Name}.", ex);
			}
		}

		if (underlyingType == typeof(Guid) && rawValue is string guidString)
		{
			try
			{
				return Guid.Parse(guidString);
			}
			catch (Exception ex) when (ex is FormatException or OverflowException)
			{
				throw new InvalidOperationException($"Tool parameter '{parameterName}' could not be converted to {targetType.Name}.", ex);
			}
		}

		if (underlyingType.IsEnum && rawValue is string enumString)
		{
			try
			{
				return Enum.Parse(underlyingType, enumString, ignoreCase: true);
			}
			catch (Exception ex) when (ex is ArgumentException or OverflowException)
			{
				throw new InvalidOperationException($"Tool parameter '{parameterName}' could not be converted to {targetType.Name}.", ex);
			}
		}

		if (underlyingType == typeof(string))
		{
			return Convert.ToString(rawValue);
		}

		try
		{
			return Convert.ChangeType(rawValue, underlyingType);
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Tool parameter '{parameterName}' could not be converted to {targetType.Name}.", ex);
		}
	}

	private static bool IsNullable(ParameterInfo parameter)
	{
		var type = parameter.ParameterType;
		if (Nullable.GetUnderlyingType(type) is not null)
		{
			return true;
		}

		if (type.IsValueType)
		{
			return false;
		}

		return NullabilityContext.Create(parameter).ReadState != NullabilityState.NotNull;
	}

	private static IReadOnlyDictionary<string, ToolRegistration> BuildToolRegistrations()
	{
		var toolTypes = new[]
		{
			typeof(TaskItemTools),
			typeof(TaskListTools),
			typeof(NoteListTools),
			typeof(NoteItemTools),
			typeof(GoalTools),
			typeof(PlanningContextTools),
			typeof(StrategicReportTools),
			typeof(WeeklyReportTools),
			typeof(ReflectionReportTools),
			typeof(DateTimeTools)
		};

		var registrations = new Dictionary<string, ToolRegistration>(StringComparer.Ordinal);
		foreach (var toolType in toolTypes)
		{
			foreach (var method in toolType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
			{
				var toolAttribute = method.GetCustomAttribute<McpServerToolAttribute>();
				if (toolAttribute is null || string.IsNullOrWhiteSpace(toolAttribute.Name))
				{
					continue;
				}

				var methodDescription = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
				var schema = BuildJsonSchema(method);
				var definition = new McpToolDefinition(toolAttribute.Name, methodDescription, schema);
				registrations.Add(toolAttribute.Name, new ToolRegistration(toolType, method, definition));
			}
		}

		return registrations;
	}

	private static JsonElement BuildJsonSchema(MethodInfo method)
	{
		var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
		var required = new List<string>();

		foreach (var parameter in method.GetParameters())
		{
			if (parameter.ParameterType == typeof(CancellationToken))
			{
				continue;
			}
			properties[parameter.Name!] = BuildParameterSchema(parameter.ParameterType);
			if (!parameter.HasDefaultValue && !IsNullable(parameter))
			{
				required.Add(parameter.Name!);
			}
		}

		var schema = new Dictionary<string, object?>
		{
			["type"] = "object",
			["properties"] = properties
		};

		if (required.Count > 0)
		{
			schema["required"] = required;
		}

		return JsonSerializer.SerializeToElement(schema, JsonSerializerOptions);
	}

	internal static Dictionary<string, object?> BuildParameterSchema(Type parameterType)
	{
		var underlyingType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
		var schema = new Dictionary<string, object?>();

		if (TryGetCollectionElementType(underlyingType, out var elementType))
		{
			schema["type"] = "array";
			schema["items"] = BuildParameterSchema(elementType);
			return schema;
		}

		if (underlyingType == typeof(Guid))
		{
			schema["type"] = "string";
			schema["format"] = "uuid";
			return schema;
		}

		if (underlyingType == typeof(bool))
		{
			schema["type"] = "boolean";
			return schema;
		}

		if (underlyingType == typeof(int) || underlyingType == typeof(long) || underlyingType == typeof(short))
		{
			schema["type"] = "integer";
			return schema;
		}

		if (underlyingType == typeof(float) || underlyingType == typeof(double) || underlyingType == typeof(decimal))
		{
			schema["type"] = "number";
			return schema;
		}

		schema["type"] = "string";
		return schema;
	}

	private static bool TryGetCollectionElementType(Type type, out Type elementType)
	{
		if (type == typeof(string))
		{
			elementType = null!;
			return false;
		}

		if (type.IsArray)
		{
			elementType = type.GetElementType()!;
			return true;
		}

		var enumerableType = type
			.GetInterfaces()
			.Prepend(type)
			.FirstOrDefault(interfaceType =>
				interfaceType.IsGenericType &&
				interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>));

		if (enumerableType is not null)
		{
			elementType = enumerableType.GetGenericArguments()[0];
			return true;
		}

		elementType = null!;
		return false;
	}

	private sealed record ToolRegistration(Type ToolType, MethodInfo Method, McpToolDefinition Definition);
}

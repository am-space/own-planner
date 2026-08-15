using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OwnPlanner.Application.Inbox;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Reporting;
using OwnPlanner.Infrastructure.Repositories;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed class DirectToolMcpAdapterTests : IDisposable
{
	private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "ownplanner-direct-tool-tests", Guid.NewGuid().ToString("N"));

	[Fact]
	public async Task ListToolDetailsAsync_ReturnsRegisteredToolDefinitions()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var serviceProvider = BuildServiceProvider();
		await using var adapter = CreateAdapter(serviceProvider);

		var toolDetails = await adapter.ListToolDetailsAsync(ct);

		toolDetails.Should().Contain(tool => tool.Name == "datetime_get_current");
		toolDetails.Should().Contain(tool => tool.Name == "taskitem_get");
		toolDetails.Should().Contain(tool => tool.Name == "strategic_report_get");

		var taskGetTool = toolDetails.Single(tool => tool.Name == "taskitem_get");
		taskGetTool.JsonSchema.Should().NotBeNull();
		taskGetTool.JsonSchema!.Value.GetProperty("required").EnumerateArray().Select(item => item.GetString()).Should().Contain("id");

		var reportTool = toolDetails.Single(tool => tool.Name == "strategic_report_get");
		var reportProperties = reportTool.JsonSchema!.Value.GetProperty("properties");
		reportProperties.TryGetProperty("taskSampleLimit", out _).Should().BeTrue();
		reportProperties.TryGetProperty("noteSampleLimit", out _).Should().BeTrue();
		reportProperties.TryGetProperty("cancellationToken", out _).Should().BeFalse();
	}

	[Fact]
	public async Task ModeConfigs_AllowedAndPreloadTools_AreRealRegisteredTools()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var serviceProvider = BuildServiceProvider();
		await using var adapter = CreateAdapter(serviceProvider);

		var registered = (await adapter.ListToolDetailsAsync(ct)).Select(tool => tool.Name).ToHashSet();
		// Delegated agents are built-in chat capabilities, not MCP registrations.
		registered.Add("search_agent_call");
		registered.Add("task_planning_agent_call");

		foreach (var (mode, config) in OwnPlanner.Application.Chat.ModeConfig.All)
		{
			config.AllowedTools.Should().OnlyContain(
				name => registered.Contains(name),
				"mode {0} should only allow real tools (guards against typos)", mode);
			config.PreloadTools.Should().OnlyContain(
				name => registered.Contains(name),
				"mode {0} should only preload real tools", mode);
		}
	}

	[Fact]
	public async Task ListToolDetailsAsync_MarksNonNullableReferenceParametersAsRequired()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var serviceProvider = BuildServiceProvider();
		await using var adapter = CreateAdapter(serviceProvider);

		var toolDetails = await adapter.ListToolDetailsAsync(ct);
		var createTool = toolDetails.Single(tool => tool.Name == "tasklist_create");

		createTool.JsonSchema.Should().NotBeNull();
		createTool.JsonSchema!.Value.GetProperty("required")
			.EnumerateArray()
			.Select(item => item.GetString())
			.Should()
			.Contain(["contextId", "title"]);
	}

	[Fact]
	public async Task CallToolAsync_DatetimeTool_ReturnsSerializedJsonPayload()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var serviceProvider = BuildServiceProvider();
		await using var adapter = CreateAdapter(serviceProvider);

		var json = await adapter.CallToolAsync("datetime_get_current", cancellationToken: ct);
		using var document = JsonDocument.Parse(json);

		document.RootElement.TryGetProperty("utc", out _).Should().BeTrue();
		document.RootElement.TryGetProperty("local", out _).Should().BeTrue();
		document.RootElement.TryGetProperty("timestamp", out _).Should().BeTrue();
	}

	[Fact]
	public async Task CallToolAsync_TaskTool_BindsGuidArgumentAndInvokesService()
	{
		var ct = TestContext.Current.CancellationToken;
		var taskId = Guid.NewGuid();
		var taskListId = Guid.NewGuid();
		var taskService = Substitute.For<ITaskItemService>();
		taskService.GetAsync(taskId, Arg.Any<CancellationToken>())
			.Returns(new TaskItemDto(taskId, "Review planning", null, false, false, DateTime.UtcNow, DateTime.UtcNow, null, null, taskListId, null, null));

		await using var serviceProvider = BuildServiceProvider(taskService: taskService);
		await using var adapter = CreateAdapter(serviceProvider);
		var arguments = new Dictionary<string, object?>
		{
			["id"] = ParseJsonElement($"\"{taskId}\"")
		};

		var json = await adapter.CallToolAsync("taskitem_get", arguments, ct);
		using var document = JsonDocument.Parse(json);

		document.RootElement.GetProperty("title").GetString().Should().Be("Review planning");
		await taskService.Received(1).GetAsync(taskId, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task CallToolAsync_StrategicReport_IsIsolatedByAdapterUser()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var serviceProvider = BuildTenantServiceProvider();
		await SeedUserTaskAsync("user-a", "User A private task", ct);
		await SeedUserTaskAsync("user-b", "User B private task", ct);
		await using var adapterA = CreateAdapter(serviceProvider, "user-a");
		await using var adapterB = CreateAdapter(serviceProvider, "user-b");

		var reportA = await adapterA.CallToolAsync("strategic_report_get", cancellationToken: ct);
		var reportB = await adapterB.CallToolAsync("strategic_report_get", cancellationToken: ct);

		reportA.Should().Contain("User A private task").And.NotContain("User B private task");
		reportB.Should().Contain("User B private task").And.NotContain("User A private task");
	}

	[Fact]
	public async Task TaskPlanningDelegation_CannotResolveAnotherUsersTaskListScope()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var serviceProvider = BuildTenantServiceProvider();
		await SeedUserTaskAsync("user-a", "User A private task", ct);
		var userBListId = await SeedUserTaskAsync("user-b", "User B private task", ct);
		await using var adapterA = CreateAdapter(serviceProvider, "user-a");

		var act = () => OwnPlanner.Application.Chat.TaskPlanningMcpAdapter.CreateAsync(adapterA, null, userBListId, ct);

		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found in the authenticated planner*");
	}

	[Fact]
	public async Task CallToolAsync_TaskListCreate_RejectsNullForNonNullableTitle()
	{
		var ct = TestContext.Current.CancellationToken;
		var taskListService = Substitute.For<ITaskListService>();

		await using var serviceProvider = BuildServiceProvider(taskListService: taskListService);
		var adapter = CreateAdapter(serviceProvider);
		var arguments = new Dictionary<string, object?>
		{
			["contextId"] = ParseJsonElement($"\"{Guid.NewGuid()}\""),
			["title"] = ParseJsonElement("null")
		};

		try
		{
			InvalidOperationException? exception = null;
			try
			{
				await adapter.CallToolAsync("tasklist_create", arguments, ct);
			}
			catch (InvalidOperationException ex)
			{
				exception = ex;
			}

			exception.Should().NotBeNull();
			exception!.Message.Should().Match("*title*cannot be null*");
			await taskListService.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
		}
		finally
		{
			await adapter.DisposeAsync();
		}
	}

	[Fact]
	public async Task CallToolAsync_TaskTool_WithInvalidGuid_ThrowsParameterSpecificInvalidOperationException()
	{
		var ct = TestContext.Current.CancellationToken;
		var taskService = Substitute.For<ITaskItemService>();

		await using var serviceProvider = BuildServiceProvider(taskService: taskService);
		var adapter = CreateAdapter(serviceProvider);
		var arguments = new Dictionary<string, object?>
		{
			["id"] = ParseJsonElement("\"not-a-guid\"")
		};

		try
		{
			InvalidOperationException? exception = null;
			try
			{
				await adapter.CallToolAsync("taskitem_get", arguments, ct);
			}
			catch (InvalidOperationException ex)
			{
				exception = ex;
			}

			exception.Should().NotBeNull();
			exception!.Message.Should().Match("*id*Guid*");
			await taskService.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
		}
		finally
		{
			await adapter.DisposeAsync();
		}
	}

	[Fact]
	public void BuildParameterSchema_StringArray_ReturnsArrayOfStrings()
	{
		var schema = DirectToolMcpAdapter.BuildParameterSchema(typeof(string[]));

		schema["type"].Should().Be("array");
		var items = schema["items"].Should().BeOfType<Dictionary<string, object?>>().Subject;
		items["type"].Should().Be("string");
	}

	[Fact]
	public void BuildParameterSchema_ListOfGuid_ReturnsArrayOfUuidStrings()
	{
		var schema = DirectToolMcpAdapter.BuildParameterSchema(typeof(List<Guid>));

		schema["type"].Should().Be("array");
		var items = schema["items"].Should().BeOfType<Dictionary<string, object?>>().Subject;
		items["type"].Should().Be("string");
		items["format"].Should().Be("uuid");
	}

	[Fact]
	public void BuildParameterSchema_EnumerableOfInt_ReturnsArrayOfIntegers()
	{
		var schema = DirectToolMcpAdapter.BuildParameterSchema(typeof(IEnumerable<int>));

		schema["type"].Should().Be("array");
		var items = schema["items"].Should().BeOfType<Dictionary<string, object?>>().Subject;
		items["type"].Should().Be("integer");
	}

	public void Dispose()
	{
		if (Directory.Exists(_tempDirectory))
		{
			Directory.Delete(_tempDirectory, recursive: true);
		}
	}

	private ServiceProvider BuildServiceProvider(ITaskItemService? taskService = null, ITaskListService? taskListService = null)
	{
		Directory.CreateDirectory(_tempDirectory);
		var inboxSeeder = Substitute.For<IInboxSeeder>();
		inboxSeeder.SeedAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var services = new ServiceCollection();
		services.AddLogging();
		services.AddHttpContextAccessor();
		services.AddSingleton<IPlannerSessionContextAccessor, PlannerSessionContextAccessor>();
		services.AddSingleton<PerUserAppInitializationService>();
		services.AddScoped<IPlannerDbContextFactory>(_ => new FixedPathTestPlannerDbContextFactory(Path.Combine(_tempDirectory, "planner.db")));
		services.AddScoped(_ => inboxSeeder);
		if (taskService is not null)
		{
			services.AddScoped(_ => taskService);
		}
		if (taskListService is not null)
		{
			services.AddScoped(_ => taskListService);
		}

		return services.BuildServiceProvider();
	}

	private static DirectToolMcpAdapter CreateAdapter(ServiceProvider serviceProvider)
		=> CreateAdapter(serviceProvider, "user-456");

	private static DirectToolMcpAdapter CreateAdapter(ServiceProvider serviceProvider, string userId)
	{
		return new DirectToolMcpAdapter(
			"session-123",
			userId,
			serviceProvider.GetRequiredService<IServiceScopeFactory>(),
			serviceProvider.GetRequiredService<IPlannerSessionContextAccessor>(),
			serviceProvider.GetRequiredService<PerUserAppInitializationService>(),
			serviceProvider.GetRequiredService<ILogger<DirectToolMcpAdapter>>());
	}

	private ServiceProvider BuildTenantServiceProvider()
	{
		Directory.CreateDirectory(_tempDirectory);
		var inboxSeeder = Substitute.For<IInboxSeeder>();
		inboxSeeder.SeedAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddHttpContextAccessor();
		services.AddSingleton<IPlannerSessionContextAccessor, PlannerSessionContextAccessor>();
		services.AddSingleton<PerUserAppInitializationService>();
		services.AddScoped<IPlannerDbContextFactory, SessionBoundTestPlannerDbContextFactory>();
		services.AddScoped(_ => inboxSeeder);
		services.AddSingleton(TimeProvider.System);
		services.AddScoped<IStrategicReportReader, StrategicReportReader>();
		services.AddScoped<ITaskListRepository, TaskListRepository>();
		services.AddScoped<ITaskItemRepository, TaskItemRepository>();
		services.AddScoped<ITaskListService, TaskListService>();
		services.AddScoped<ITaskItemService, TaskItemService>();
		services.AddSingleton(new TenantTestDirectory(_tempDirectory));
		return services.BuildServiceProvider();
	}

	private async Task<Guid> SeedUserTaskAsync(string userId, string title, CancellationToken cancellationToken)
	{
		var path = Path.Combine(_tempDirectory, $"ownplanner-user-{userId}.db");
		await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options);
		await db.Database.MigrateAsync(cancellationToken);
		var list = new TaskList("Tasks");
		db.AddRange(list, new TaskItem(title, list.Id));
		await db.SaveChangesAsync(cancellationToken);
		return list.Id;
	}

	private static JsonElement ParseJsonElement(string json)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}

	private sealed class FixedPathTestPlannerDbContextFactory(string dbPath) : IPlannerDbContextFactory
	{
		private readonly string _dbPath = dbPath;

		public ValueTask<AppDbContext> CreateAsync(CancellationToken cancellationToken = default)
		{
			var options = new DbContextOptionsBuilder<AppDbContext>()
				.UseSqlite($"Data Source={_dbPath}")
				.Options;
			return ValueTask.FromResult(new AppDbContext(options));
		}

		public Task DeleteUserDatabaseAsync(string userId, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}

	private sealed record TenantTestDirectory(string Path);

	private sealed class SessionBoundTestPlannerDbContextFactory(
		TenantTestDirectory directory,
		IPlannerSessionContextAccessor sessionContextAccessor) : IPlannerDbContextFactory
	{
		public ValueTask<AppDbContext> CreateAsync(CancellationToken cancellationToken = default)
		{
			var userId = sessionContextAccessor.Current?.UserId ?? throw new UnauthorizedAccessException();
			var path = System.IO.Path.Combine(directory.Path, $"ownplanner-user-{userId}.db");
			return ValueTask.FromResult(new AppDbContext(
				new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options));
		}

		public Task DeleteUserDatabaseAsync(string userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}

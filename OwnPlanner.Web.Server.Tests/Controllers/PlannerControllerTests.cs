using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using OwnPlanner.Application.Common;
using OwnPlanner.Application.Planner;
using OwnPlanner.Mcp.Tools;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Web.Server.Controllers;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Controllers;

public class PlannerControllerTests
{
	private const string UserId = "11111111-1111-1111-1111-111111111111";
	private readonly IPlannerReadService _plannerReadService = Substitute.For<IPlannerReadService>();
	private readonly ITaskItemService _taskItemService = Substitute.For<ITaskItemService>();
	private readonly IPerUserAppInitializationService _initializationService = Substitute.For<IPerUserAppInitializationService>();

	[Fact]
	public async Task GetTasks_InitializesTenantAndMapsQuery()
	{
		var expected = new PagedResult<PlannerTaskSummaryDto>([], 0, 10, 20);
		_plannerReadService.QueryTasksAsync(Arg.Any<PlannerTaskQuery>(), Arg.Any<CancellationToken>())
			.Returns(expected);
		var controller = CreateController();

		var result = await controller.GetTasks(
			"launch",
			PlannerTaskStatus.All,
			important: true,
			taskListId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
			contextId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
			goalId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
			offset: 10,
			limit: 20,
			cancellationToken: TestContext.Current.CancellationToken);

		result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(expected);
		Received.InOrder(() =>
		{
			_initializationService.EnsureInitializedAsync(
				Arg.Is<SessionContext>(context => context != null && context.UserId == UserId),
				Arg.Any<CancellationToken>());
			_plannerReadService.QueryTasksAsync(
				Arg.Is<PlannerTaskQuery>(query =>
					query != null
					&& query.Search == "launch"
					&& query.Status == PlannerTaskStatus.All
					&& query.ImportantOnly
					&& query.Offset == 10
					&& query.Limit == 20),
				Arg.Any<CancellationToken>());
		});
	}

	[Fact]
	public async Task GetTask_UnknownItem_ReturnsNotFound()
	{
		_plannerReadService.GetTaskAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns((PlannerTaskDetailDto?)null);
		var controller = CreateController();

		var result = await controller.GetTask(Guid.NewGuid(), TestContext.Current.CancellationToken);

		result.Result.Should().BeOfType<NotFoundResult>();
	}

	[Fact]
	public async Task GetFilterOptions_ReturnsCurrentTenantMetadata()
	{
		var expected = new PlannerFilterOptionsDto([], [], [], []);
		_plannerReadService.GetFilterOptionsAsync(Arg.Any<CancellationToken>()).Returns(expected);
		var controller = CreateController();

		var result = await controller.GetFilterOptions(TestContext.Current.CancellationToken);

		result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(expected);
		await _initializationService.Received(1).EnsureInitializedAsync(
			Arg.Is<SessionContext>(context => context != null && context.UserId == UserId),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task RestoreTask_WhenOriginalListIsMissing_ReturnsConflict()
	{
		var id = Guid.NewGuid();
		_taskItemService.RestoreAsync(id, Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new InvalidOperationException("Original task list no longer exists.")));
		var controller = CreateController();

		var result = await controller.RestoreTask(id, TestContext.Current.CancellationToken);

		result.Should().BeOfType<ConflictObjectResult>();
	}

	[Fact]
	public async Task PermanentlyDeleteTask_DelegatesOnlyToGuardedApplicationOperation()
	{
		var id = Guid.NewGuid();
		var controller = CreateController();

		var result = await controller.PermanentlyDeleteTask(id, TestContext.Current.CancellationToken);

		result.Should().BeOfType<OkObjectResult>();
		await _taskItemService.Received(1).PermanentlyDeleteAsync(id, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task PermanentlyDeleteTask_WhenTaskDoesNotExist_ReturnsNotFound()
	{
		var id = Guid.NewGuid();
		_taskItemService.PermanentlyDeleteAsync(id, Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new KeyNotFoundException()));

		var result = await CreateController().PermanentlyDeleteTask(id, TestContext.Current.CancellationToken);

		result.Should().BeOfType<NotFoundResult>();
	}

	[Theory]
	[InlineData(-1, 25)]
	[InlineData(0, 0)]
	[InlineData(0, 101)]
	public async Task GetTaskTrash_InvalidPaging_ReturnsBadRequest(int offset, int limit)
	{
		var result = await CreateController().GetTaskTrash(offset, limit, TestContext.Current.CancellationToken);

		result.Result.Should().BeOfType<BadRequestObjectResult>();
		await _taskItemService.DidNotReceive().ListTrashedPagedAsync(
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
	}

	private PlannerController CreateController()
	{
		var principal = new ClaimsPrincipal(new ClaimsIdentity(
		[
			new Claim(ClaimTypes.NameIdentifier, UserId),
			new Claim("SessionId", "planner-test-session")
		], "TestAuth"));
		return new PlannerController(_plannerReadService, _taskItemService, _initializationService)
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext { User = principal }
			}
		};
	}
}

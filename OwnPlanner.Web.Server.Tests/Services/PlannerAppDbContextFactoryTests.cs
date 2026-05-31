using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;
using OwnPlanner.Web.Server.Services;
using System.Security.Claims;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed class PlannerAppDbContextFactoryTests : IDisposable
{
	private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "ownplanner-web-db-factory-tests", Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		if (Directory.Exists(_tempDirectory))
		{
			Directory.Delete(_tempDirectory, recursive: true);
		}
	}

	[Fact]
	public void RepositoryResolution_DoesNotThrow_WhenScopeIsUnauthenticated()
	{
		var services = CreateServices();

		using var serviceProvider = services.BuildServiceProvider();
		using var scope = serviceProvider.CreateScope();

		var repository = scope.ServiceProvider.GetRequiredService<ITaskItemRepository>();
		repository.Should().NotBeNull();
	}

	[Fact]
	public async Task RepositoryCall_ThrowsUnauthorizedAccessException_WhenScopeIsUnauthenticated()
	{
		var services = CreateServices();

		using var serviceProvider = services.BuildServiceProvider();
		using var scope = serviceProvider.CreateScope();
		var repository = scope.ServiceProvider.GetRequiredService<ITaskItemRepository>();

		var act = async () => await repository.ListAsync(includeCompleted: true, TestContext.Current.CancellationToken);

		await act.Should().ThrowAsync<UnauthorizedAccessException>();
	}

	[Fact]
	public async Task RepositoryCall_UsesAuthenticatedHttpUser_WhenPlannerSessionScopeIsMissing()
	{
		var services = CreateServices();

		using var serviceProvider = services.BuildServiceProvider();
		using var scope = serviceProvider.CreateScope();
		var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
		httpContextAccessor.HttpContext = new DefaultHttpContext
		{
			User = new ClaimsPrincipal(new ClaimsIdentity(
			[
				new Claim(ClaimTypes.NameIdentifier, "user-123")
			],
				"TestAuth"))
		};

		var dbContextFactory = scope.ServiceProvider.GetRequiredService<IPlannerDbContextFactory>();
		await using (var dbContext = await dbContextFactory.CreateAsync(TestContext.Current.CancellationToken))
		{
			await dbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
		}

		var repository = scope.ServiceProvider.GetRequiredService<ITaskItemRepository>();
		var items = await repository.ListAsync(includeCompleted: true, TestContext.Current.CancellationToken);

		items.Should().BeEmpty();
		File.Exists(Path.Combine(_tempDirectory, "ownplanner-user-user-123.db")).Should().BeTrue();
	}

	private ServiceCollection CreateServices()
	{
		Directory.CreateDirectory(_tempDirectory);
		var services = new ServiceCollection();
		services.AddHttpContextAccessor();
		services.AddSingleton<IPlannerSessionContextAccessor, PlannerSessionContextAccessor>();
		services.AddScoped<IPlannerDbContextFactory>(serviceProvider =>
			new PlannerAppDbContextFactory(
				_tempDirectory,
				serviceProvider.GetRequiredService<IPlannerSessionContextAccessor>(),
				serviceProvider.GetRequiredService<IHttpContextAccessor>()));
		services.AddScoped<ITaskItemRepository, TaskItemRepository>();
		return services;
	}
}





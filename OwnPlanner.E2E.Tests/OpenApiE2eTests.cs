using System.Net;
using System.Text.Json;
using FluentAssertions;
using OwnPlanner.E2E.Tests.Infrastructure;

namespace OwnPlanner.E2E.Tests;

[Collection(E2eCollection.Name)]
[Trait("Category", "E2E")]
public sealed class OpenApiE2eTests
{
	[Fact]
	public async Task DevelopmentEndpoint_ServesOpenApiDocumentWithExistingRoutes()
	{
		await using var application = new E2eWebApplicationFactory("Development");
		await application.InitializeAsync();
		using var client = application.CreateClient();
		using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
		using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
		document.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.");
		document.RootElement.GetProperty("info").GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
		var paths = document.RootElement.GetProperty("paths");
		paths.GetProperty("/api/Auth/register").GetProperty("post").GetProperty("responses").TryGetProperty("200", out _).Should().BeTrue();
		paths.TryGetProperty("/api/planner/tasks", out _).Should().BeTrue();
	}
}

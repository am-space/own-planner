using FluentAssertions;
using Microsoft.Playwright;
using OwnPlanner.E2E.Tests.Infrastructure;

namespace OwnPlanner.E2E.Tests;

[Collection(E2eCollection.Name)]
[Trait("Category", "E2E")]
public sealed class HostSmokeTests(E2eWebApplicationFactory application) : E2ePageTest(application)
{
	[Fact]
	public async Task ProtectedChatRoute_RedirectsUnauthenticatedVisitorToSignIn()
	{
		var temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Application.TemporaryRoot)) + Path.DirectorySeparatorChar;
		Path.GetFullPath(Application.AuthDatabasePath).Should().StartWith(temporaryRoot);
		Path.GetFullPath(Application.UserDatabaseDirectory).Should().StartWith(temporaryRoot);

		var response = await Page.GotoAsync("/chat");

		response.Should().NotBeNull();
		response!.Ok.Should().BeTrue();
		await Expect(Page).ToHaveURLAsync(new Regex("/login$"));
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sign In" })).ToBeVisibleAsync();
	}
}

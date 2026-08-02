using Microsoft.Playwright;
using OwnPlanner.E2E.Tests.Infrastructure;

namespace OwnPlanner.E2E.Tests;

[Collection(E2eCollection.Name)]
[Trait("Category", "E2E")]
public sealed class AuthenticationE2eTests(E2eWebApplicationFactory application) : E2ePageTest(application)
{
	[Fact]
	public async Task RegistrationCreatesAuthenticatedSession_AndLogoutRemovesProtectedAccess()
	{
		var user = CreateUser();
		await RegisterAsync(Page, user);

		await Expect(Page.GetByText(user.Username, new() { Exact = true })).ToBeVisibleAsync();
		await Page.GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true }).ClickAsync();

		await Expect(Page).ToHaveURLAsync(new Regex("/login$"));
		await Page.GotoAsync("/chat");
		await Expect(Page).ToHaveURLAsync(new Regex("/login$"));
	}
}

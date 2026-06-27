using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OwnPlanner.Domain.Users;
using OwnPlanner.Web.Server.Authentication;

namespace OwnPlanner.Web.Server.Tests.Authentication;

/// <summary>
/// Exercises <see cref="CookiePrincipalValidator"/> through the real cookie authentication middleware
/// (TestServer), covering what the unit tests can't: that a deleted user's cookie actually stops
/// granting access, and that deleting your own account really clears your cookie (the cookie handler
/// suppresses the validator's <c>ShouldRenew</c> when the same request signs out).
/// </summary>
public sealed class CookiePrincipalValidatorIntegrationTests : IDisposable
{
	private const string CookieName = "OwnPlanner.Auth";

	private readonly StrongBox<bool> _userExists = new(true);
	private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
	private readonly TestServer _server;
	private readonly HttpClient _client;

	public CookiePrincipalValidatorIntegrationTests()
	{
		_userRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns(_ => _userExists.Value ? new User("user@example.com", "tester", "hash") : null);

		var builder = new WebHostBuilder()
			.UseTestServer()
			.ConfigureServices(services =>
			{
				services.AddRouting();
				services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
					.AddCookie(options =>
					{
						options.Cookie.Name = CookieName;
						options.Events.OnValidatePrincipal = CookiePrincipalValidator.ValidateAsync;
						// Mirror the app: return 401 for unauthenticated API calls instead of redirecting.
						options.Events.OnRedirectToLogin = context =>
						{
							context.Response.StatusCode = StatusCodes.Status401Unauthorized;
							return Task.CompletedTask;
						};
					});
				services.AddAuthorization();
				services.AddScoped(_ => _userRepository);
			})
			.Configure(app =>
			{
				app.UseRouting();
				app.UseAuthentication();
				app.UseAuthorization();
				app.UseEndpoints(endpoints =>
				{
					endpoints.MapPost("/signin", async context =>
					{
						var claims = new[] { new Claim(ClaimTypes.NameIdentifier, context.Request.Query["userId"].ToString()) };
						var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
						await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
					});

					endpoints.MapGet("/protected", () => Results.Ok("ok")).RequireAuthorization();

					endpoints.MapPost("/delete-self", async context =>
					{
						_userExists.Value = false;
						await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
					}).RequireAuthorization();
				});
			});

		_server = new TestServer(builder);
		_client = _server.CreateClient();
	}

	public void Dispose()
	{
		_client.Dispose();
		_server.Dispose();
	}

	[Fact]
	public async Task ValidCookie_GrantsAccess()
	{
		var cookie = await SignInAsync();

		var response = await GetAsync("/protected", cookie);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task DeletedUser_CookieFromAnotherSession_IsRejected()
	{
		// The (unstamped) cookie this session holds keeps re-validating against the DB.
		var cookie = await SignInAsync();
		(await GetAsync("/protected", cookie)).StatusCode.Should().Be(HttpStatusCode.OK);

		// Another session deletes the account.
		_userExists.Value = false;

		(await GetAsync("/protected", cookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task DeletingOwnAccount_ClearsTheCookie()
	{
		var cookie = await SignInAsync();

		// Delete-self validates (user still present → stamp + ShouldRenew), then signs out. The cookie
		// handler must drop the cookie, not resurrect it via the pending renewal.
		var deleteResponse = await PostAsync("/delete-self", cookie);
		deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

		// Follow the cookie the server handed back (a deletion cookie if sign-out won; a stamped,
		// still-valid cookie if renewal incorrectly won). With the renewal suppressed it is empty,
		// so the next request is unauthenticated.
		var resultingCookie = ExtractAuthCookie(deleteResponse);
		(await GetAsync("/protected", resultingCookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	private async Task<string> SignInAsync()
	{
		var response = await _client.PostAsync($"/signin?userId={Guid.NewGuid()}", content: null);
		response.EnsureSuccessStatusCode();
		return ExtractAuthCookie(response);
	}

	private Task<HttpResponseMessage> GetAsync(string path, string cookie) => SendAsync(HttpMethod.Get, path, cookie);

	private Task<HttpResponseMessage> PostAsync(string path, string cookie) => SendAsync(HttpMethod.Post, path, cookie);

	private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string cookie)
	{
		var request = new HttpRequestMessage(method, path);
		request.Headers.Add("Cookie", cookie);
		return _client.SendAsync(request);
	}

	// Returns the "OwnPlanner.Auth=<value>" pair from the response's Set-Cookie (value is empty for a
	// deletion cookie), suitable for echoing back as a Cookie header.
	private static string ExtractAuthCookie(HttpResponseMessage response)
	{
		var setCookie = response.Headers.TryGetValues("Set-Cookie", out var values)
			? values.FirstOrDefault(v => v.StartsWith($"{CookieName}=", StringComparison.Ordinal))
			: null;

		setCookie.Should().NotBeNull("the response should set the auth cookie");
		return setCookie!.Split(';')[0];
	}
}

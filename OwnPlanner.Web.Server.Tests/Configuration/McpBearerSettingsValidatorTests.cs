using FluentAssertions;
using OwnPlanner.Web.Server.Configuration;

namespace OwnPlanner.Web.Server.Tests.Configuration;

public sealed class McpBearerSettingsValidatorTests
{
	private readonly McpBearerSettingsValidator _validator = new();

	[Fact]
	public void Validate_Fails_WhenTokenIsMissing()
	{
		var result = _validator.Validate(
			name: null,
			new McpBearerSettings
			{
				TokenBindings =
				[
					new McpBearerTokenBinding
					{
						Token = "",
						UserId = "user-1"
					}
				]
			});

		result.Succeeded.Should().BeFalse();
		result.Failures.Should().Contain(message => message.Contains("Token must not be empty.", StringComparison.Ordinal));
	}

	[Fact]
	public void Validate_Fails_WhenTokenIsNull()
	{
		var result = _validator.Validate(
			name: null,
			new McpBearerSettings
			{
				TokenBindings =
				[
					new McpBearerTokenBinding
					{
						Token = null!,
						UserId = "user-1"
					}
				]
			});

		result.Succeeded.Should().BeFalse();
		result.Failures.Should().Contain(message => message.Contains("Token must not be empty.", StringComparison.Ordinal));
	}

	[Fact]
	public void Validate_Fails_WhenUserIdIsMissing()
	{
		var result = _validator.Validate(
			name: null,
			new McpBearerSettings
			{
				TokenBindings =
				[
					new McpBearerTokenBinding
					{
						Token = "token-1",
						UserId = "   "
					}
				]
			});

		result.Succeeded.Should().BeFalse();
		result.Failures.Should().Contain(message => message.Contains("UserId must not be empty.", StringComparison.Ordinal));
	}

	[Fact]
	public void Validate_Fails_WhenUserIdIsNull()
	{
		var result = _validator.Validate(
			name: null,
			new McpBearerSettings
			{
				TokenBindings =
				[
					new McpBearerTokenBinding
					{
						Token = "token-1",
						UserId = null!
					}
				]
			});

		result.Succeeded.Should().BeFalse();
		result.Failures.Should().Contain(message => message.Contains("UserId must not be empty.", StringComparison.Ordinal));
	}

	[Fact]
	public void Validate_Fails_WhenTokenIsDuplicated()
	{
		var result = _validator.Validate(
			name: null,
			new McpBearerSettings
			{
				TokenBindings =
				[
					new McpBearerTokenBinding
					{
						Token = "token-1",
						UserId = "user-1"
					},
					new McpBearerTokenBinding
					{
						Token = "token-1",
						UserId = "user-2"
					}
				]
			});

		result.Succeeded.Should().BeFalse();
		result.Failures.Should().Contain(message => message.Contains("Token is duplicated.", StringComparison.Ordinal));
	}

	[Fact]
	public void Validate_Fails_WhenUserIdIsDuplicated()
	{
		var result = _validator.Validate(
			name: null,
			new McpBearerSettings
			{
				TokenBindings =
				[
					new McpBearerTokenBinding
					{
						Token = "token-1",
						UserId = "user-1"
					},
					new McpBearerTokenBinding
					{
						Token = "token-2",
						UserId = "user-1"
					}
				]
			});

		result.Succeeded.Should().BeFalse();
		result.Failures.Should().Contain(message => message.Contains("UserId is duplicated.", StringComparison.Ordinal));
	}

	[Fact]
	public void Validate_Succeeds_WhenBindingsAreValid()
	{
		var result = _validator.Validate(
			name: null,
			new McpBearerSettings
			{
				TokenBindings =
				[
					new McpBearerTokenBinding
					{
						Token = "token-1",
						UserId = "user-1"
					}
				]
			});

		result.Succeeded.Should().BeTrue();
	}
}

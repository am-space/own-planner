using System.Reflection;
using FluentAssertions;
using OwnPlanner.Infrastructure.Adapters;

namespace OwnPlanner.Infrastructure.Tests.Adapters;

public class McpAdapterTests
{
	[Fact]
	public void Constructor_CoalescesNullArgumentsToEmptyArray()
	{
		string[]? arguments = null;

		var adapter = new McpAdapter("dotnet", arguments!);
		var field = typeof(McpAdapter).GetField("_arguments", BindingFlags.Instance | BindingFlags.NonPublic);

		field.Should().NotBeNull();
		var storedArguments = field!.GetValue(adapter).Should().BeOfType<string[]>().Subject;
		storedArguments.Should().BeEmpty();
	}
}

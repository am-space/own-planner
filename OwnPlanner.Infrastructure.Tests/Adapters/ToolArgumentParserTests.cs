using System.Text.Json;
using FluentAssertions;
using OwnPlanner.Infrastructure.Adapters;

namespace OwnPlanner.Infrastructure.Tests.Adapters;

public class ToolArgumentParserTests
{
	[Fact]
	public void GetStringArgument_Returns_Null_When_Arguments_Are_Missing()
	{
		var result = ToolArgumentParser.GetStringArgument(null, "query");

		result.Should().BeNull();
	}

	[Fact]
	public void GetStringArgument_Returns_String_Value_As_Is()
	{
		var arguments = new Dictionary<string, object?>
		{
			["query"] = "latest .NET 10 news"
		};

		var result = ToolArgumentParser.GetStringArgument(arguments, "query");

		result.Should().Be("latest .NET 10 news");
	}

	[Fact]
	public void GetStringArgument_Returns_String_From_JsonElement()
	{
		var arguments = new Dictionary<string, object?>
		{
			["query"] = ParseJsonElement("\"latest .NET 10 news\"")
		};

		var result = ToolArgumentParser.GetStringArgument(arguments, "query");

		result.Should().Be("latest .NET 10 news");
	}

	[Fact]
	public void GetStringArgument_Returns_Number_From_JsonElement_As_String()
	{
		var arguments = new Dictionary<string, object?>
		{
			["query"] = ParseJsonElement("42")
		};

		var result = ToolArgumentParser.GetStringArgument(arguments, "query");

		result.Should().Be("42");
	}

	[Fact]
	public void GetStringArgument_Returns_Clr_Number_As_Invariant_String()
	{
		var arguments = new Dictionary<string, object?>
		{
			["query"] = 12.5m
		};

		var result = ToolArgumentParser.GetStringArgument(arguments, "query");

		result.Should().Be("12.5");
	}

	[Fact]
	public void GetStringArgument_Throws_For_Json_Object()
	{
		var arguments = new Dictionary<string, object?>
		{
			["query"] = ParseJsonElement("{\"term\":\"latest .NET 10 news\"}")
		};

		var act = () => ToolArgumentParser.GetStringArgument(arguments, "query");

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*query*JSON Object*");
	}

	[Fact]
	public void GetStringArgument_Throws_For_Json_Array()
	{
		var arguments = new Dictionary<string, object?>
		{
			["query"] = ParseJsonElement("[\"latest\",\"news\"]")
		};

		var act = () => ToolArgumentParser.GetStringArgument(arguments, "query");

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*query*JSON Array*");
	}

	[Fact]
	public void GetStringArgument_Throws_For_Unsupported_Clr_Type()
	{
		var arguments = new Dictionary<string, object?>
		{
			["query"] = new Version(10, 0)
		};

		var act = () => ToolArgumentParser.GetStringArgument(arguments, "query");

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*query*Version*");
	}

	private static JsonElement ParseJsonElement(string json)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}
}

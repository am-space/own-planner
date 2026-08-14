using System.Text.Json;
using FluentAssertions;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed class ToolResultJsonTests
{
	private static TaskItemDto SampleTask(string? description = null, DateTime? dueAt = null, Guid? goalId = null) => new(
		Id: Guid.NewGuid(),
		Title: "Buy milk",
		Description: description,
		IsCompleted: false,
		IsImportant: true,
		CreatedAt: new DateTime(2026, 6, 10, 8, 0, 0, DateTimeKind.Utc),
		UpdatedAt: new DateTime(2026, 6, 12, 9, 0, 0, DateTimeKind.Utc),
		DueAt: dueAt,
		CompletedAt: null,
		TaskListId: Guid.NewGuid(),
		FocusAt: null,
		GoalId: goalId);

	private static JsonElement Serialize(object value) =>
		JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(value, ToolResultJson.Options));

	[Fact]
	public void Serialize_DropsAuditTimestamps()
	{
		var json = Serialize(SampleTask());

		json.TryGetProperty("createdAt", out _).Should().BeFalse();
		json.TryGetProperty("updatedAt", out _).Should().BeFalse();
	}

	[Fact]
	public void Serialize_OmitsNullFields()
	{
		var json = Serialize(SampleTask(description: null, dueAt: null, goalId: null));

		json.TryGetProperty("description", out _).Should().BeFalse();
		json.TryGetProperty("dueAt", out _).Should().BeFalse();
		json.TryGetProperty("goalId", out _).Should().BeFalse();
		json.TryGetProperty("completedAt", out _).Should().BeFalse();
	}

	[Fact]
	public void Serialize_KeepsFunctionalFields()
	{
		var due = new DateTime(2026, 6, 14, 15, 0, 0, DateTimeKind.Utc);
		var json = Serialize(SampleTask(description: "2% organic", dueAt: due));

		json.GetProperty("title").GetString().Should().Be("Buy milk");
		json.GetProperty("description").GetString().Should().Be("2% organic");
		json.GetProperty("isImportant").GetBoolean().Should().BeTrue();
		json.TryGetProperty("id", out _).Should().BeTrue();
		json.TryGetProperty("taskListId", out _).Should().BeTrue();
	}

	[Fact]
	public void Serialize_KeepsStrategicNoteLastUpdatedAt()
	{
		var lastUpdatedAt = new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);
		var sample = new StrategicNoteSample(
			Guid.NewGuid(), "Decision", "Preview", false, true, lastUpdatedAt,
			Guid.NewGuid(), null, null);

		var json = Serialize(sample);

		json.GetProperty("lastUpdatedAt").GetDateTime().Should().Be(lastUpdatedAt);
	}

	[Fact]
	public void Serialize_KeepsDueAtInIsoFormat()
	{
		var due = new DateTime(2026, 6, 14, 15, 0, 0, DateTimeKind.Utc);
		var json = Serialize(SampleTask(dueAt: due));

		json.GetProperty("dueAt").GetDateTime().Should().Be(due);
		// ISO 8601 round-trips through System.Text.Json's default DateTime handling.
		json.GetProperty("dueAt").GetString().Should().StartWith("2026-06-14T15:00:00");
	}

	[Fact]
	public void Serialize_AppliesToListElements()
	{
		var json = Serialize(new List<TaskItemDto> { SampleTask(), SampleTask() });

		json.ValueKind.Should().Be(JsonValueKind.Array);
		foreach (var element in json.EnumerateArray())
		{
			element.TryGetProperty("createdAt", out _).Should().BeFalse();
			element.TryGetProperty("updatedAt", out _).Should().BeFalse();
		}
	}

	[Fact]
	public void Serialize_EmitsNonAsciiAsUtf8_NotEscaped()
	{
		var raw = JsonSerializer.Serialize(SampleTask(description: "Забронировать"), ToolResultJson.Options);

		raw.Should().Contain("Забронировать");
		raw.Should().NotContain("\\u04");
	}
}

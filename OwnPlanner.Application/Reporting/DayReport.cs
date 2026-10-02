using OwnPlanner.Application.Calendar;

namespace OwnPlanner.Application.Reporting;

/// <summary>Daily overview or fresh pages of every matching today/overdue task.</summary>
public sealed record DayReportOptions(string Section = "all", int Offset = 0, int Limit = 5)
{
	public void Validate()
	{
		if (Section is not ("all" or "today" or "overdue")) throw new ArgumentException("section must be all, today or overdue.");
		if (Offset < 0) throw new ArgumentException("offset must be non-negative.");
		if (Limit is < 1 or > 20) throw new ArgumentException("limit must be between 1 and 20.");
		if (Section == "all" && Offset != 0) throw new ArgumentException("Use a named section when paging with offset.");
	}
}

/// <summary>Read-only daily execution metadata; samples are bounded, counts are exact and period is always local today.</summary>
public sealed record DayReport(PlanningPeriod Period, string View, IReadOnlyList<DayReportSection> Sections,
	IReadOnlyList<DayReportTask> Tasks, int TitleCharacterLimit);

/// <summary>MatchCount includes tasks displayed elsewhere; TotalCount is assigned count in all, every match in named views.
/// Truncated signals any omitted matches, HasMore additional tasks after this page.</summary>
public sealed record DayReportSection(string Name, int MatchCount, int TotalCount, int Offset, int Limit,
	bool Truncated, bool HasMore, IReadOnlyList<Guid> TaskIds);

/// <summary>Execution metadata with all daily reasons; flexible stored focus dates remain distinct from UTC deadlines and their local dates.</summary>
public sealed record DayReportTask(Guid Id, string Title, bool TitleTruncated, bool IsImportant,
	DateOnly? FocusDate, DateTime? DueAt, DateOnly? DueDate, IReadOnlyList<string> Reasons);

/// <summary>Pure daily classification and deterministic sampling; ordering is not an automatic execution priority decision.</summary>
public static class DayReportBuilder
{
	public static DayReport Build(PlanningPeriod today, IReadOnlyList<GeneralTaskRow> tasks, DayReportOptions? options = null)
	{
		options ??= new();
		options.Validate();
		if (today.Name != "today") throw new ArgumentException("Daily execution requires the today calendar period.");
		var zone = TimeZoneInfo.FindSystemTimeZoneById(today.TimeZone);
		var candidates = tasks.Where(t => !t.IsCompleted).Select(t => Classify(t, today, zone))
			.Where(t => t.Reasons.Count > 0).ToList();
		var sections = new List<DayReportSection>();
		var samples = new List<DayReportTask>();
		foreach (var section in options.Section == "all" ? new[] { "today", "overdue" } : [options.Section])
		{
			var matches = candidates.Where(t => Matches(section, t)).ToList();
			var selected = (options.Section == "all" && section == "overdue" ? matches.Where(t => !Matches("today", t)) : matches)
				.OrderBy(t => t.DueAt ?? DateTime.MaxValue).ThenByDescending(t => t.IsImportant)
				.ThenBy(t => t.FocusDate).ThenBy(t => t.Id).ToList();
			var page = selected.Skip(options.Offset).Take(options.Limit).ToList();
			sections.Add(new(section, matches.Count, selected.Count, options.Offset, options.Limit,
				page.Count < selected.Count, options.Offset + page.Count < selected.Count, page.Select(t => t.Id).ToArray()));
			samples.AddRange(page);
		}
		return new(today, options.Section, sections, samples, GeneralReportBuilder.TitleLimit);
	}

	private static bool Matches(string section, DayReportTask task) => section == "today"
		? task.Reasons.Contains("plannedToday") || task.Reasons.Contains("dueToday")
		: task.Reasons.Contains("overdueDeadline");

	private static DayReportTask Classify(GeneralTaskRow task, PlanningPeriod today, TimeZoneInfo zone)
	{
		var focus = task.FocusAt.HasValue ? DateOnly.FromDateTime(task.FocusAt.Value) : (DateOnly?)null;
		var dueAt = task.DueAt.HasValue ? DateTime.SpecifyKind(task.DueAt.Value, DateTimeKind.Utc) : (DateTime?)null;
		var dueDate = dueAt.HasValue ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(dueAt.Value, zone)) : (DateOnly?)null;
		var reasons = new List<string>();
		if (focus == today.Today) reasons.Add("plannedToday");
		if (dueAt >= today.StartsAtUtc && dueAt < today.EndsAtUtc) reasons.Add("dueToday");
		if (dueAt < today.AsOfUtc) reasons.Add("overdueDeadline");
		return new(task.Id, GeneralReportBuilder.Clip(task.Title), task.Title.Length > GeneralReportBuilder.TitleLimit,
			task.IsImportant, focus, dueAt, dueDate, reasons);
	}
}

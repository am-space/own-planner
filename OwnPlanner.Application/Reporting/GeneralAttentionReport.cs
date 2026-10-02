using OwnPlanner.Application.Calendar;

namespace OwnPlanner.Application.Reporting;

/// <summary>Current-week attention options. All displays deduplicated sections; a named section pages every matching task.</summary>
public sealed record GeneralAttentionOptions(string Section = "all", int Offset = 0, int Limit = 5)
{
	public void Validate()
	{
		if (Section != "all" && !GeneralAttentionBuilder.SectionNames.Contains(Section, StringComparer.Ordinal))
			throw new ArgumentException("section must be all, today, earlierThisWeek, remainingWeek, overdue or olderFocus.");
		if (Offset < 0) throw new ArgumentException("offset must be non-negative.");
		if (Limit is < 1 or > 20) throw new ArgumentException("limit must be between 1 and 20.");
		if (Section == "all" && Offset != 0) throw new ArgumentException("Use a named section when paging with offset.");
	}
}

/// <summary>Bounded current-state attention. Period is always thisWeek; UTC deadline instants and stored focus dates remain distinct.</summary>
public sealed record GeneralAttentionReport(PlanningPeriod Period, PlanningPeriod TodayPeriod, string View,
	IReadOnlyList<GeneralAttentionSection> Sections, IReadOnlyList<GeneralAttentionTask> Tasks, int TitleCharacterLimit);

/// <summary>
/// MatchCount counts all tasks matching this section's reasons, including tasks displayed elsewhere.
/// TotalCount counts assigned tasks in the all view, or every match in a named-section view.
/// Truncated means the page omits any tasks; HasMore means additional tasks follow this page.
/// Days counts the week's sections by display date, independently of bounded samples; overdue and olderFocus omit day breakdowns.
/// </summary>
public sealed record GeneralAttentionSection(string Name, int MatchCount, int TotalCount, int Offset, int Limit,
	bool Truncated, bool HasMore, IReadOnlyList<Guid> TaskIds, IReadOnlyList<GeneralAttentionDay> Days);
public sealed record GeneralAttentionDay(DateOnly Date, int TotalCount);

/// <summary>One displayed task with all selection reasons, stored focus date, UTC deadline and local deadline date.</summary>
public sealed record GeneralAttentionTask(Guid Id, string Title, bool TitleTruncated, DateOnly? FocusDate,
	DateTime? DueAt, DateOnly? DueDate, DateOnly? DisplayDate, IReadOnlyList<string> Reasons);

/// <summary>Pure attention classification and sampling using the shared resolved calendar, independent of persistence.</summary>
public static class GeneralAttentionBuilder
{
	internal static readonly string[] SectionNames = ["today", "earlierThisWeek", "remainingWeek", "overdue", "olderFocus"];

	public static GeneralAttentionReport Build(PlanningPeriod week, IReadOnlyList<GeneralTaskRow> tasks,
		GeneralAttentionOptions? options = null)
	{
		options ??= new();
		options.Validate();
		if (week.Name != "thisWeek") throw new ArgumentException("Attention requires the thisWeek calendar period.");
		var today = PlanningCalendarResolver.Resolve("today", week.AsOfUtc, week.TimeZone, week.WeekStart)
			with { FallbackExplanation = week.FallbackExplanation };
		var zone = TimeZoneInfo.FindSystemTimeZoneById(week.TimeZone);
		var candidates = tasks.Where(t => !t.IsCompleted).Select(t => Classify(t, week, today, zone))
			.Where(t => t.Reasons.Count > 0).ToList();
		var sections = new List<GeneralAttentionSection>();
		var samples = new List<GeneralAttentionTask>();
		foreach (var section in options.Section == "all" ? SectionNames : [options.Section])
		{
			var matches = candidates.Where(t => Matches(section, t.Reasons)).ToList();
			var selected = (options.Section == "all"
				? matches.Where(t => SectionNames.First(s => Matches(s, t.Reasons)) == section)
				: matches).Select(t => t with { DisplayDate = DisplayDate(section, t, today.Today) })
				.OrderBy(t => t.DisplayDate).ThenBy(t => t.DueAt ?? DateTime.MaxValue).ThenBy(t => t.FocusDate).ThenBy(t => t.Id).ToList();
			var limit = options.Section == "all" && section == "olderFocus" ? 0 : options.Limit;
			var page = selected.Skip(options.Offset).Take(limit).ToList();
			sections.Add(new(section, matches.Count, selected.Count, options.Offset, limit,
				page.Count < selected.Count, options.Offset + page.Count < selected.Count,
				page.Select(t => t.Id).ToArray(), selected.Where(t => t.DisplayDate.HasValue && (section is "today" or "earlierThisWeek" or "remainingWeek"))
					.GroupBy(t => t.DisplayDate!.Value).OrderBy(g => g.Key).Select(g => new GeneralAttentionDay(g.Key, g.Count())).ToArray()));
			samples.AddRange(page);
		}
		return new(week, today, options.Section, sections, samples, GeneralReportBuilder.TitleLimit);
	}

	private static GeneralAttentionTask Classify(GeneralTaskRow task, PlanningPeriod week, PlanningPeriod today, TimeZoneInfo zone)
	{
		var focus = task.FocusAt.HasValue ? DateOnly.FromDateTime(task.FocusAt.Value) : (DateOnly?)null;
		// SQLite materialization can lose Kind; deadlines are stored UTC instants, including on the wire.
		var dueAt = task.DueAt.HasValue ? DateTime.SpecifyKind(task.DueAt.Value, DateTimeKind.Utc) : (DateTime?)null;
		var due = dueAt.HasValue ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(dueAt.Value, zone)) : (DateOnly?)null;
		var reasons = new List<string>();
		if (focus == today.Today) reasons.Add("plannedToday");
		if (task.DueAt >= today.StartsAtUtc && task.DueAt < today.EndsAtUtc) reasons.Add("dueToday");
		if (focus >= week.StartDate && focus < today.Today) reasons.Add("missedFocusThisWeek");
		if (focus > today.Today && focus < week.EndExclusiveDate) reasons.Add("plannedLaterThisWeek");
		if (task.DueAt >= today.EndsAtUtc && task.DueAt < week.EndsAtUtc) reasons.Add("dueLaterThisWeek");
		if (task.DueAt < week.AsOfUtc) reasons.Add("overdueDeadline");
		if (focus < week.StartDate) reasons.Add("olderFocus");
		return new(task.Id, GeneralReportBuilder.Clip(task.Title), task.Title.Length > GeneralReportBuilder.TitleLimit,
			focus, dueAt, due, null, reasons);
	}

	private static bool Matches(string section, IReadOnlyList<string> reasons) => section switch
	{
		"today" => reasons.Contains("plannedToday") || reasons.Contains("dueToday"),
		"earlierThisWeek" => reasons.Contains("missedFocusThisWeek"),
		"remainingWeek" => reasons.Contains("plannedLaterThisWeek") || reasons.Contains("dueLaterThisWeek"),
		"overdue" => reasons.Contains("overdueDeadline"),
		"olderFocus" => reasons.Contains("olderFocus"),
		_ => false
	};

	private static DateOnly? DisplayDate(string section, GeneralAttentionTask task, DateOnly today) => section switch
	{
		"today" => today,
		"earlierThisWeek" or "olderFocus" => task.FocusDate,
		"overdue" => task.DueDate,
		"remainingWeek" => new[] { task.Reasons.Contains("plannedLaterThisWeek") ? task.FocusDate : null,
			task.Reasons.Contains("dueLaterThisWeek") ? task.DueDate : null }.Where(d => d.HasValue).Min(),
		_ => null
	};
}

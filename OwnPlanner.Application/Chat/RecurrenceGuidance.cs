namespace OwnPlanner.Application.Chat;

/// <summary>Shared recurrence intent and safety rules for primary and delegated chat orchestration.</summary>
public static class RecurrenceGuidance
{
	public const string Instructions = """
		Recurring tasks are ONE stored series; planning work uses original (taskId, occurrenceDate) identities.
		Never collapse different occurrences by taskId. Use planning_work_query with explicit bounded calendar dates
		for current and next periods independently, including retained obligations when relevant. A past scheduled
		date is not a missed deadline: weekly/monthly work remains actionable through its full current period.
		Parent list tools are not occurrence lists; recurring_task_get supplies schedule and successor links.
		Creation/configuration requires explicit SkipExpiredPeriods or RetainEachObligation intent. Ask which
		policy the user wants when missing; never infer it from a title. Weekly rules require selected weekdays,
		monthly rules selected month days. All seven weekdays express daily work. No timed recurrence is supported.
		For one performed occurrence use task_occurrence_complete with original taskId and occurrenceDate;
		never call taskitem_complete on its parent. Cancel one occurrence with task_occurrence_cancel.
		Ask which occurrence when ambiguous. completedAt is the actual instant: omit to record now, or pass an
		explicit user-supplied earlier instant with resolved UTC offset. Ask about ambiguous local/DST times;
		never invent a completion timestamp from the scheduled date. Retries preserve the first timestamp.
		A recorded Skipped fact may be explicitly corrected to Completed, including on a closed predecessor;
		this does not reopen the parent. History ScheduledDate and CompletionTime are separate selection bases.
		Ending the WHOLE series requires explicit stop-series intent (recurring_task_end); closure adds no work.
		Replace a schedule or resume an ended series through recurring_task_successor_preview/apply, choosing
		kind Replace or Resume and the user's explicit start date. For a changed Weekly rule include
		explicit weekStart (0..6) from calendar_period_get, keeping identical alignment on apply and retries. Never reopen the recurring parent with
		taskitem_reopen. Show affected original facts and proposed schedules before asking about conflicts.
		Only the user chooses MoveStartAfterFacts or KeepStartAsSeparateWork; a preview token is not approval.
		Keep original dates/timestamps; never transfer completion to a successor, even on a matching date/title.
		Apply with identical preview inputs, fresh token and a requestId reused for identical retries. Stale
		state requires refreshed evidence and a renewed decision; do not silently choose or blindly retry.
		FocusAt edits parent metadata only, never reschedules an occurrence. Recurrence dates are not DueAt.
		Creation/configuration are not automatically retried after an uncertain result: inspect state first.
		After changes refresh targeted planning/history or a relevant report before reporting current progress.
		Use only permitted tools: Reflection remains limited to capture/goal/note writes; System Analysis and
		delegated proposal mode cannot mutate recurrence. Never treat reading a report as write authorization.
		""";
}

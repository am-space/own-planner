# Recurring tasks acceptance evidence

This maps [#84](https://github.com/am-space/own-planner/issues/84) and final stage
[#96](https://github.com/am-space/own-planner/issues/96) to executable evidence. The eight stages ship
with tests throughout; this final stage adds presentation/recovery verification rather than replacing
earlier tests. Current behavior is described in [recurring tasks](recurring-tasks.md) and
[ADR-0043](adr/0043-recurring-series-planner-readiness.md).

## Parent acceptance map

Paths below are repository-relative. Each named class contains focused cases for the listed behavior.

| Agreed requirement | Evidence |
| --- | --- |
| One persisted parent, common fields, independent optional deadline/focus | Domain `RecurringTaskItemTests`; Application `RecurringTaskManagementTests`; Infrastructure `FridayRemainsActionableAndExpiresOnlyAtItsOriginalWeekBoundary` field-copy assertions; browser `RecurringPlannerE2eTests` |
| Weekly interval/selected weekdays/all weekdays, captured week start | Application `RecurrenceCalculatorTests`, `RecurrenceCalendarTests`; Domain `RecurrenceContractTests` |
| Monthly intervals, 29/30/31 clamp/deduplicate, leap years and original selections | Same calculator/contract suites; Infrastructure `MonthlyPersistenceClampsAndDeduplicatesWithoutLosingTheOriginalSelection` and `MonthlyDowntimeClosureCorrectionAndRestartedResume_PreserveDatesTimesAndAnEmptyBreak` |
| Start/end inclusivity, no work outside generation bounds, explicitly empty retired interval | Calculator tests; successor `EarlierEndIsNeverExtendedAndEmptyRetiredRangeRoundTripsAndCloses`; projection `EmptyRetiredGenerationHasNoWorkButPreservedFactsRemainReachable` |
| Local calendar arithmetic, timezone/DST and configured week starts without DateOnly shifting | `RecurrenceCalendarTests`; lifecycle `ReconciliationUsesLocalDstBoundaryRatherThanUtcMidnight`; projection/history, report and review DST cases |
| Current/next week or month coexist; Sunday does not expire Friday | Projection `SundayShowsCurrentAndNextFridayAndReadsNeverReconcileExpiredHistory`; review `SundayReviewKeepsCurrentAndNextOccurrencesActionableWithoutClosingSeries`; calculator two-period tests |
| Complete/cancel one composite identity versus explicitly ending a series | Lifecycle `StoredTaskCompletionUsesSharedTerminationAndOrdinaryCompletionStillRecordsWork`; review `OrdinaryReviewActionsCannotAccidentallyMutateSeries`; real MCP transport flow and shared tool schema tests |
| Explicit missed policy; no title inference; full-period skip versus retained obligations | Application management and MCP parser tests; scripted `RecurrenceConversationTests`; lifecycle `DowntimeCatchupRespectsPolicyAndFullPeriodBeforeClosure`; projection `MonthlyClampingAndFullPeriodExpiryAreSharedWithTheEngine` |
| Late/early completion, original scheduled date versus actual UTC instant, first timestamp on retry | Lifecycle `DateOnlyAndCompletionRoundTripAsSeparateValuesWithUniqueIdentity`, `LateDefaultCompletionCorrectsSkipAndRetriesKeepFirstActualInstant`; real transport and account browser flows |
| Scheduled-date results versus actual activity; local half-open report windows; no duplicate parent closure work | Projection `HistorySelectsOriginalDatesOrActualActivityWithExactHalfOpenPages`; `RecurringReportReaderTests` all six reader kinds; review `FridayCompletedMondayUsesActivityInstantAndKeepsOriginalScheduledIdentity` |
| Explicit skipped correction, including automatically closed and predecessor history outside shortened bounds | Lifecycle `AutomaticallyClosedHistoryCanBeCorrectedWithoutReopeningParent`, `ClosedHistoricalSkipOutsideGenerationBoundsKeepsOriginalIdentityOnCorrection`; report/review correction cases; monthly integration |
| Completion/correction/expiry races, cancellation conflicts, idempotency and atomic rollback | Lifecycle `ConcurrentExpiryAndCompletionsConvergeOnFirstSuccessfulTimestamp`, `FailedTransactionRollsBackFactAndCheckpointTogether`; review concurrent guard cases; revision failure/retry tests |
| Bounded automatic closure only after end/resolution; unbounded never auto-closes | Lifecycle `BoundedRetainedSeriesClosesOnlyAfterEveryDateIsResolvedAndEndHasPassed`, `SkipPolicyCanCloseAfterEarlyResolutionsButNeverBeforeTheEndDatePasses`, `EmptyBoundClosesWithoutAFactAndUnboundedSeriesNeverAutoCloses` |
| Downtime catch-up/checkpoint restart; no implicit writes during reads | Lifecycle `CommittedBatchesResumeAfterFailureAndNeverRevertPriorFacts`, calendar-change and read-only tests; monthly integration; Planner store fact-count assertions |
| Atomic linked replacement, preserved prior obligations, copied common fields with independent dates | `RecurringTaskSuccessorTests`; projection `ShortenedPredecessorFactsStayInHistoryAndAreNotRegeneratedAsActiveWork`; Planner store and desktop/mobile link navigation |
| Future completed/cancelled fact conflicts, both explicit choices, no transferred or inferred completion | Successor `BothChoicesPreserveOriginalCompletionAndCancellationFactsWithoutTransferringWork`; HTTP/direct uses KeepStartAsSeparateWork and stdio uses MoveStartAfterFacts |
| Stale choices/schedules and successful retry keys; no duplicate successor | Successor stale/concurrent/rollback cases; real MCP `HttpAndDirect_RecurrenceFlowAndEveryToolRespectBearerTenantIsolation`, `Stdio_RecurrenceFlowPreservesHistoryAcrossRestartAndUserFiles` |
| Resume explicitly/automatically ended series by linked successor, empty break, ordinary reopen compatibility | Successor `ExplicitAndAutomaticClosureResumeWithoutGapWorkOrReopeningHistory`, `RestoringTrashedSeriesRequiresExplicitResumeAndKeepsTheBreakEmpty`; lifecycle ordinary reopen; monthly integration |
| Exact combined filtering/order/counts/paging; complete retained prefix without persisted pending rows | `PlanningWorkReaderTests.MixedPagesHaveExactCountsStableTiesDistinctSourcesAndNoMissingIdentity`, `DecadesOfRetainedWorkStayReachableAcrossPagesWithoutPendingRows`; report bounded sample/group cases; independent review occurrence pages |
| Completed/Trash/archived-list exclusions while permitted closed history remains available | Projection `ActiveExclusionsPreserveOnlyPermittedHistory`; report `EveryWorkViewExcludesUnavailableSourcesAndTheirFacts`; review eligibility and Planner outstanding flags |
| General/Week/Day preloads include occurrences; Reflection actual activity; strategic source/work units | `RecurringReportReaderTests`; Web `EveryModePreloadIncludesOriginalOccurrenceIdentitiesAndOnlyItsOwnTenant`; six-mode scripted/real shared report invocation tests and mode permission snapshots |
| Shared authenticated in-process, HTTP and stdio behavior, dates/enums/schemas, no tenant/path args | `RecurringTaskToolsTests`; real `McpTransportE2eTests.Recurrence`; direct configuration/correction tests; delegated scope/proposal denial tests |
| Weekly Review frozen calendar/selection, exact source/work units and guarded occurrence actions | `WeeklyReviewTests.Recurrence`; shared MCP and HTTP controller/tenant coverage; browser `RecurringReviewShowsMixedProgressAndOneRowPerSeries` |
| One browser row per series, rule/bounds/policy, predecessor/successor, outstanding work, mobile/deep links | `PlannerReadStoreTests.RecurrenceMetadata_PagesStoredSeriesAndFlagsOnlyActionablePreviousWork` (nine cases); browser `SeriesWorkspace_ShowsOneRowAndLinkedOutstandingWork_AndRestoresWithoutResuming` (desktop/mobile) |
| Existing database upgrades and ordinary task compatibility | Lifecycle `MigrationPreservesAllOrdinaryFieldsAndAddsOnlyNullableConfigurationAndZeroRevision`; successor `MigrationAddsNullLinksWithoutChangingExistingSeriesOrOccurrenceHistory`; unchanged ordinary task/Planner/transport/browser suites |
| Trash/restore, permanent deletion and independent series survival | Lifecycle `TrashRestorePreservesFactsAndClosureAndPermanentDeletionCascadesHistory`, `StoredTrashCatchesUpBeforeStoppingEvenInArchivedListsWithoutGeneratingInactiveGap`; successor permanent-removal case; desktop/mobile recovery browser flow |
| Export/reopen snapshot and account deletion with two-user isolation | Browser `AccountExportAndDeletion_PreserveOnlyTheAuthenticatedUsersSeriesAndFacts`: real cookie-authenticated SQLite exports, preserved dates/links/facts/retry ledger, no foreign rows, database/auth deletion for A with B still accessible; existing account/controller cleanup suites |

## Lifecycle-entry audit

| Entry point | Recurring-series behavior |
| --- | --- |
| `TaskItemService.CompleteAsync` / `taskitem_complete` | Shared EndSeries, not performed occurrence work; tool description explicitly says whole series |
| `task_occurrence_complete/cancel` | Explicit TaskId/date; shared atomic lifecycle, no parent completion |
| `recurring_task_end` | Administrative ending, no additional completed work |
| `TaskItemService.ReopenAsync` / `taskitem_reopen` | Domain rejects recurring parent reopen; caller is directed to explicit successor Resume |
| Weekly Review ordinary actions | Reject recurring parents; guarded occurrence actions use original composite identity and fresh token |
| Task deletion/Trash/restore | Catch up and end before Trash; preserve facts and links; restore keeps closure; permanent deletion cascades only the removed series' facts/request metadata |
| Planner Tasks/inspector | Read-only; explicit series-ended and occurrence-action guidance, no ambiguous completion/reopen control |
| Planner Trash | Restore/permanent delete only; explicit recurring restore guidance |
| Browser Weekly Review | Finish/skip/defer controls act on review state, never occurrence completion or series termination |
| Chat modes/delegation/Telegram/Console | Shared capability/recurrence guidance and shared tool paths; proposals cannot mutate; no new occurrence-to-parent completion route |

## Verification interpretation

Run `./scripts/setup.sh` then `./scripts/verify.sh --all` for frontend lint/build, all backend projects
and deterministic browser/real MCP transport E2E checks. The current change records actual command
results in its archived plan and PR description. Migration tests upgrade existing schema data to the
current migration head; exports reopen as independent SQLite files.

Scripted provider tests and scenario-backed browser chat exercise orchestration, tool permissions,
state transitions and refreshed results. They do **not** establish how a live Gemini model interprets
an arbitrary request. Opt-in `Category=LiveAi` checks require separate credentials/configuration and
were not run for this change. No live-model success is implied by deterministic E2E results.

Known boundaries remain documented: selected source/fact metadata memory is not constant; ordinary
creation and delegated mutation preflight retain their existing transaction boundaries. No migration
or deployment was added in final stage 8, and final verification does not publish a release.

Problem statement

taskitem_list_items (and the other taskitem_list_* tools) return the full task collection in a single, unpaginated response. On a real account this produced 113 tasks = 73,293 characters (~20k+ tokens) serialized as one JSON line. This exceeds the MCP tool-output token limit, so the result never reaches the model's context — the host had to spill it to a file. From the LLM's perspective the list is effectively unreadable.

Three compounding causes:

Cyrillic (non-ASCII) is escaped as \uXXXX. Every Russian character is emitted as a 6-char \u0417 sequence instead of ~2 UTF-8 bytes. Measured: 5,313 escape sequences ≈ 26.5k wasted characters. Switching to UTF-8 output alone drops the payload from 73,293 → 46,789 chars (−36%).
Per-item overhead. Each task carries both createdAt and updatedAt at tick precision (2026-06-18T16:21:28.2177879), plus three full GUIDs. Dropping updatedAt and reducing dates to day precision brings it to 40,153 chars (−45% vs. original).
No pagination and full description per item. Returning 113 tasks with full bodies in one call is the root cause. A list endpoint generally only needs id + title + status flags; full text belongs in taskitem_get (notes already do this — their description says "Content is truncated to a short preview").
Recommendations (in priority order)
Emit UTF-8, not ASCII-escaped JSON. In .NET set
JsonSerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
Cheapest fix, instant −36%.
Add pagination to all taskitem_list_* tools: limit (default ~25–50, hard max e.g. 100) + offset/cursor, and return totalCount / hasMore so the model knows to page. This is what actually keeps the response under the token limit regardless of account size.
Slim the list projection. Truncate description to a short preview, drop updatedAt, and reduce timestamps to date (or second) precision. Keep full content/description only in taskitem_get.
Expected combined effect: ~73k → well under 10k chars for a typical page, and bounded regardless of task count.

Sorting (default order for pagination)
Pagination is only stable if the server applies a deterministic ORDER BY. Proposed default:

focusDate ascending — overdue / earliest-planned work first.
focusDate IS NULL last — unscheduled/backlog tasks must not dominate the top of the list (in most SQL engines NULL sorts first by default, so this needs an explicit NULLS LAST or equivalent).
updatedAt descending — among same/no planned date, most recently touched first.
id (or createdAt) ascending as a final tiebreaker — guarantees a total order so offset-based paging never skips or repeats rows.
Notes / open questions:

Consider isImportant DESC as the first key if "important floats to top" is desired — but that's a product decision; it conflicts with strict chronological order. My recommendation: keep it out of the default sort and expose onlyImportant (already present) as a filter instead.
Completed tasks already excluded via includeCompleted=false — keep that.
Allow an optional sort parameter later, but ship the deterministic default first.
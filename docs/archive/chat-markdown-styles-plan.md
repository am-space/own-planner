> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0019: Scoped chat Markdown presentation](../adr/0019-scoped-chat-markdown-presentation.md).
> Details below may not reflect later refinements made during review.

# Chat and Markdown styles

Status: Implemented. Issue: #55.

Outcome: readable full Markdown reports and compact conversations across themes and viewport sizes.

## Impact and acceptance mapping

Frontend: extract MarkdownContent with scoped semantic typography, block spacing, safe GFM rendering,
keyboard-focusable table/code overflow regions, wrapping links, and theme-derived colors. Update
ChatPage message surfaces, timestamps, responsive header, suggestions and composer. Preserve actions.
Tests: deterministic browser fixture covers all heading levels, paragraphs, lists, quotes, rules,
tasks, links and code/tables; verify local versus page overflow at desktop and 320px in both themes,
keyboard newline/send, loading, clearing, mode switching and screenshots for visual inspection.
Documentation: reference and ADR for shared renderer; archive this plan on completion.
Domain, Application, Infrastructure, Web API, MCP, console/stdio and persistence: not affected.

## Order and verification

1. Follow existing ReactMarkdown + remarkGfm and MUI patterns; no new dependencies.
2. Extract renderer then style chat chrome and responsive containers.
3. Add fixture and focused browser regressions; inspect screenshots and refine.
4. Run frontend lint/build and scripts/verify.sh --all; review full diff.
5. Write ADR/reference, archive plan and hand off through a PR closing #55.

## Review

Reviewed before implementation: each issue criterion maps to frontend styling, browser assertions or
visual review above. No business rules, tenant resolution, public contracts, migrations or raw HTML
changes. Existing chat is request/response, not streaming; loading and empty responses cover current
behavior. No report parsing, prompt changes, modes or whole-app redesign. Risk: small embedded chat
panels and mobile headers require explicit width checks. Color choices use existing theme tokens,
with contrast checked for links, body and metadata on actual surfaces.

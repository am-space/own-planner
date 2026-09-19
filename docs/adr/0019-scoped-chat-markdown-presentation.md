# ADR-0019: Scoped chat Markdown presentation

**Date:** 2026-09-19
**Status:** Accepted
**Deciders:** OwnPlanner maintainers

## Context

Issue #55 identified oversized report headings, dense paragraphs and dominant message backgrounds.
Global page typography was affecting generated Markdown. Full reports and existing chat actions
must remain available in full-page, embedded and mobile chat.

## Decision

Use a reusable `MarkdownContent` component built on the existing ReactMarkdown and remarkGfm
pipeline. Scope semantic heading, paragraph, list, quote, code, table and link styles to the renderer.
Keep raw HTML disabled and the default URL transformation. Do not parse reports into a new format.

Use MUI theme tokens for surfaces, text, borders and link colors. Limit Markdown to 80ch, wrap prose
and URLs, and give wide tables and preformatted code their own keyboard-focusable scroll containers.
Keep code whitespace intact. Assistant messages use a quiet paper surface; user messages use a subtle
primary tint. Explicit speaker labels distinguish senders independently of color. Timestamps use
secondary text. Headers, suggestions and composers share these surfaces, and compact headers reserve
space for the shell's collapse control. Suggestions can scroll when they would crowd mobile chat.

## Consequences

- Reports remain semantic, selectable Markdown with no new dependency or backend contract.
- Typography is isolated from global page headings; both chat layouts share the renderer.
- Wide data requires local horizontal scrolling on small screens.
- This does not add syntax highlighting, report dashboards, exports or streaming transport support.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Web/ownplanner.web.client/src/components/MarkdownContent.tsx` | Scoped renderer |
| `OwnPlanner.Web/ownplanner.web.client/src/pages/ChatPage.tsx` | Message and chat presentation |
| `OwnPlanner.E2E.Tests/ChatE2eTests.cs` | Deterministic browser and visual fixtures |

See the [original plan](../archive/chat-markdown-styles-plan.md) and
[chat presentation reference](../chat-presentation.md).

# Chat presentation

Chat uses `MarkdownContent` for user and assistant text in the full chat page and embedded planner
assistant. It retains ReactMarkdown's default HTML and URL safety behavior and supports GFM tables,
task lists and strikethrough. Content and semantic heading levels are preserved.

Typography is scoped to the renderer: body 16px/1.65, title 28px, section heading 20.8px, descending
h3–h6 sizes, 12px paragraph gaps and 24px section gaps. Markdown has an 80ch maximum width.
Messages use 16px mobile and 24px desktop padding. Short messages retain intrinsic width.
Links are underlined with visible focus outlines. Wide tables and code blocks scroll independently;
code remains preformatted and prose wraps long unbroken strings. Raw HTML is displayed as text.

Theme tokens control colors. User messages have a primary tint and a “You” label, assistant messages
a paper surface and “OwnPlanner” label. Secondary text identifies timestamps and system changes.
The composer keeps Enter-to-send and Shift+Enter-for-newline, up to four visible input rows. Suggestions
have bounded scrolling, and the embedded header reserves room for the shell's collapse button.

## Validation and visual review

`./scripts/verify.sh --all` runs lint, production build, backend tests and deterministic browser tests.
`MarkdownReport_ContainsOverflowAndPreservesChatInteractions` exercises light/dark themes at 1280px
and 320px, without an external LLM. It covers all heading levels, paragraphs, nested lists, task
lists, emphasis, quotes, rules, inline/fenced code, links, tables, raw HTML safety, loading, empty and
short replies, newline/send, mode switching and clearing. It checks document/chat overflow,
local table/code overflow, focusability, and last-message visibility above the composer.

The test writes `chat-report-*`, `chat-details-*`, `chat-short-*` and `chat-compact-*` PNGs under
`TestResults/E2E/`. These generated files are review artifacts, not pixel baselines. Reviewed desktop
and 320px reports in both themes: title wrapping, section rhythm, metadata, local scrolling and
multiline input remain readable. Link contrast on paper is approximately 7.9:1 in light mode and
5.7:1 in dark mode; secondary text is approximately 5.7:1 and 9.4:1 respectively. Composer placeholder
text uses the secondary text token at full opacity. Browser coverage uses Chromium; native mobile keyboard behavior
and other browser engines are not covered. The existing API returns complete responses, so loading
and empty-response fixtures cover current behavior rather than a hypothetical streaming UI.

See [ADR-0019](adr/0019-scoped-chat-markdown-presentation.md).

# ADR-0005: Accept `\uXXXX`-Escaped Non-ASCII on the MCP Wire

**Date:** 2026-06-27  
**Status:** Accepted  
**Deciders:** OwnPlanner maintainers

---

## Context

Tools surfaced over the MCP hosts (the web `/mcp` endpoint and the `OwnPlanner.Mcp.StdioApp`
stdio host) return text that frequently contains non-ASCII characters — task titles, notes, and
descriptions are often written in Cyrillic and other non-Latin scripts. On the wire, every such
character is emitted as a 6-char `\uXXXX` escape sequence instead of ~2 UTF-8 bytes, roughly a 3×
inflation for non-Latin text. This was first hit as one compounding cause of the task-list token
blow-up addressed in [ADR-0004](0004-task-list-token-reduction.md); this ADR records the encoding
constraint on its own, because it applies to **every** tool that returns non-ASCII text, not only the
task-list tools.

The escaping is **not** something we can switch off by configuration. The MCP C# SDK
(`ModelContextProtocol*`, currently 1.4.0) serializes outgoing JSON-RPC in two distinct stages, and
the one that matters is not reachable by consumers:

- **Stage 1 — result → content.** A tool's return value is serialized into the call result
  (`content[].text`). This *is* configurable per tool via `WithTools(options)` (and, in the unreleased
  [csharp-sdk#925](https://github.com/modelcontextprotocol/csharp-sdk/pull/925), via a server-wide
  `McpServerOptions.JsonSerializerOptions`).
- **Stage 2 — content → wire.** The whole JSON-RPC message is written to the transport using the
  static, frozen `McpJsonUtilities.DefaultOptions`, whose `Encoder` is the default ASCII-escaping
  `JavaScriptEncoder`. **There is no global JSON-format hook for this stage** — no
  `McpServerOptions`, transport-options, or DI registration influences it, and the singleton is frozen
  before user code runs.

Because the escaping happens at stage 2, every avenue for changing it was exhausted and rejected
during the ADR-0004 work (see Alternatives). The full external write-up lives in
[`docs/upstream-issue-mcp-wire-encoder.md`](../upstream-issue-mcp-wire-encoder.md).

## Decision

**We accept `\uXXXX`-escaped non-ASCII output on the two MCP transport surfaces and do not attempt
any runtime workaround to change it.**

Concretely, as shipped:

1. **Both MCP hosts use plain `.WithTools<T>()`** — no per-tool `JsonSerializerOptions`, no reflection,
   no hand-serialized JSON. Those were all verified to be ineffective or strictly worse (Alternatives).
   `OwnPlanner.Web.Server/Program.cs` and `OwnPlanner.Mcp.StdioApp/Program.cs` each carry a comment
   recording that the SDK serializer cannot be reconfigured (confirmed on 1.1.0 and 1.4.0) and that
   payloads are kept small in the tool layer instead.
2. **The encoder override is applied only where it actually reaches the wire** — the in-process
   Gemini chat path, where we control serialization via `ToolResultJson.Options`
   (`Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`). This path does *not* go through the MCP
   transport, so it emits literal non-ASCII; the two MCP hosts do not and cannot.
3. **Payload size is controlled by other means, not by fixing the escaping** — slim projections and
   pagination per [ADR-0004](0004-task-list-token-reduction.md). The escaping is treated as a fixed
   per-character cost to design around, not a bug to defeat at runtime.
4. **The SDK stays at 1.4.0.** The bump from 1.1.0 did not change the behavior; it is kept for currency
   only.
5. **A ready-to-file upstream issue is maintained** at
   [`docs/upstream-issue-mcp-wire-encoder.md`](../upstream-issue-mcp-wire-encoder.md), asking the SDK
   to make the transport-message serializer's `Encoder` configurable.

## Consequences

### Positive

- **One honest, documented constraint.** Future contributors who see escaped non-ASCII on the `/mcp`
  or stdio wire find a recorded reason rather than re-investigating a dead end. The two `Program.cs`
  comments point at the same conclusion.
- **No fragile workarounds in the codebase** — no reflection against frozen singletons, no
  double-serialization hacks that the next SDK bump could silently break.
- **Mitigation lives at the right layer.** Token cost is bounded by slimming/pagination in the tool
  layer, which helps regardless of script and survives SDK changes.

### Negative / Trade-offs

- **Non-Latin payloads stay ~3× larger on the MCP wire.** A Cyrillic-heavy 25-item task page is
  ~37k chars on the MCP surfaces vs. ~10k on the chat path. Bounded, but not as tight as Latin text.
- **Two behaviors for "the same" tool output.** The in-process chat path emits literal UTF-8 while the
  MCP hosts emit `\uXXXX`. This asymmetry is intrinsic to where each path serializes and is
  unavoidable until the SDK changes.

### When to revisit

Revisit if the MCP C# SDK exposes a configurable serializer/`Encoder` for **transport-message**
serialization (stage 2). At that point: set the transport encoder to `UnsafeRelaxedJsonEscaping` on
both hosts, drop the two `Program.cs` notes, and re-tune projection sizes for non-Latin data. Note
that #925 alone is **not** sufficient — it threads options into stage 1 only.

## Alternatives Considered

All verified during the ADR-0004 work; full matrix in
[`docs/upstream-issue-mcp-wire-encoder.md`](../upstream-issue-mcp-wire-encoder.md).

- **Per-tool `WithTools(options)` with a relaxed `Encoder`** — 1.1.0 throws at startup (read-only
  options need a `TypeInfoResolver`); 1.4.0 boots but the wire stays escaped. Governs stage 1, not
  stage 2.
- **ASP.NET `ConfigureHttpJsonOptions` / Minimal-API JSON options** — wrong pipeline; MCP does not
  serialize messages through it.
- **Hand-serializing JSON inside the tool and returning the string** — strictly worse; the string is
  re-escaped as a JSON value (every `"` becomes `\"`).
- **Reflection on `McpJsonUtilities.DefaultOptions`** — mutating the encoder in place has no effect
  (writer options cached at first use); replacing the static instance is CLR-blocked (`initonly`
  static field).
- **Waiting on [csharp-sdk#925](https://github.com/modelcontextprotocol/csharp-sdk/pull/925)** — adds
  `McpServerOptions.JsonSerializerOptions` but threads it into stage 1 (`*.Create`) only; would not fix
  stage-2 wire escaping even once released.
- **Chosen: accept the escaping, override only on the chat path, mitigate size elsewhere** — the only
  option that is correct today and free of fragile runtime hacks.

## Deferred

- **Switch to literal UTF-8 on the MCP wire once the SDK supports it.** This decision is explicitly
  provisional: the moment the MCP C# SDK exposes a way to configure the **transport-message** (stage 2)
  serializer's `Encoder` — globally or per transport — we will adopt it, set the encoder to
  `UnsafeRelaxedJsonEscaping` on both hosts, remove the two `Program.cs` constraint notes, and re-tune
  the projection sizes for non-Latin data. Tracked by
  [`docs/upstream-issue-mcp-wire-encoder.md`](../upstream-issue-mcp-wire-encoder.md);
  [csharp-sdk#925](https://github.com/modelcontextprotocol/csharp-sdk/pull/925) alone does **not**
  qualify, as it only covers stage 1.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Web/OwnPlanner.Web.Server/Program.cs` | Plain `.WithTools<T>()` + note on the non-configurable SDK serializer |
| `OwnPlanner.Mcp.StdioApp/Program.cs` | Same constraint note on the stdio host |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/ToolResultJson.cs` | `UnsafeRelaxedJsonEscaping` encoder — the one surface where the override takes effect (chat path) |
| `docs/upstream-issue-mcp-wire-encoder.md` | Prepared upstream issue + full matrix of rejected workarounds |
| `docs/adr/0004-task-list-token-reduction.md` | Broader decision that first surfaced this constraint and bounds payload size around it |

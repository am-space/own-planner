# Prepared upstream issue — MCP C# SDK

> **Not yet filed.** Ready-to-post issue for
> [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk). Paste the
> body below into a new issue when ready. Context for *us* is in
> [`task-list-token-reduction-plan.md`](task-list-token-reduction-plan.md) (step 6); this file is the
> external-facing report, written to stand alone.

---

**Title:** Tool output is always `\uXXXX`-escaped on the wire — transport message serializer (`Encoder`) is not configurable

**Labels (suggested):** enhancement, serialization

---

## Summary

Non-ASCII text returned by a tool is always emitted as `\uXXXX` escape sequences in the JSON-RPC
message sent to the client, and there is no supported way to change this. The server serializes
outgoing protocol messages with the static, frozen `McpJsonUtilities.DefaultOptions`, whose `Encoder`
is the default (ASCII-escaping) `JavaScriptEncoder`. None of the configuration surfaces a consumer can
reach affect this encoder.

For non-Latin scripts the cost is ~3× on the wire: each character becomes a 6-char `\uXXXX` sequence
instead of ~2 UTF-8 bytes. For tools that return lists, this inflates payloads enough to push them
past client-side tool-output token limits.

## Why this is distinct from #636 / #925

This is easy to conflate with [#636](https://github.com/modelcontextprotocol/csharp-sdk/issues/636)
and [#925](https://github.com/modelcontextprotocol/csharp-sdk/pull/925), but it is a **different
serialization stage**:

- **Stage 1 — result → content.** The tool's return value is serialized into the call result
  (`content[].text` / structured content). This is governed by per-tool `WithTools(options)` and, in
  #925, by the new server-wide `McpServerOptions.JsonSerializerOptions`. `#636` (NaN/Infinity via
  `NumberHandling`) is a stage-1 problem, which is why passing options to `WithTools` fixes it.
- **Stage 2 — content → wire.** The whole JSON-RPC message is written to the transport. This is done
  with `McpJsonUtilities.DefaultOptions`, and its `Encoder` is what escapes strings on the wire.

The `Encoder`/escaping problem is **stage 2**. #925's diff only threads options into
`McpServerTool/Prompt/Resource.Create(...)` (stage 1), so it does not address this — and the title
string ends up re-escaped when the result is written into the outgoing message regardless of what
stage-1 options were used.

## Repro

Minimal stdio server (SDK 1.4.0, .NET 10):

```csharp
[McpServerToolType]
public static class DemoTool
{
    [McpServerTool, System.ComponentModel.Description("Returns a Cyrillic string.")]
    public static object Echo() => new { title = "Задача" };
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<DemoTool>();
await builder.Build().RunAsync();
```

Drive it:

```
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"x","version":"1"}}}
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"Echo","arguments":{}}}
```

Observed on the wire (the `text` content is the serialized result):

```
..."text":"{"title":"Задача"}"...
```

Expected (desired, with a relaxed encoder): literal `"title":"Задача"`.

## Things that do *not* work (verified)

| Attempt | Result |
|---|---|
| `WithTools<T>(new JsonSerializerOptions { Encoder = UnsafeRelaxedJsonEscaping })` | 1.1.0: throws at startup (`JsonSerializerOptions ... read-only`, needs a `TypeInfoResolver`); 1.4.0: starts but wire stays escaped |
| `AddMcpServer` / `McpServerOptions` | no serializer/encoder property exists (≤ 1.4.0) |
| ASP.NET `ConfigureHttpJsonOptions(...)` | wrong pipeline — MCP doesn't serialize via Minimal-API JSON options |
| Hand-serializing JSON in the tool and returning the string | worse — the string is re-escaped as a JSON value (`"` for every quote) |
| Reflection: unfreeze `DefaultOptions` + set `Encoder` | set succeeds but no effect (writer options already cached at first use) |
| Reflection: replace the static `DefaultOptions` instance | CLR blocks it (`FieldAccessException: Cannot set initonly static field`) |

## Root cause

The session/transport writes JSON-RPC messages using `McpJsonUtilities.DefaultOptions`, a get-only
singleton frozen before user code runs. There is no `McpServerOptions` hook, transport-options hook, or
DI registration that influences the serializer used for **message** serialization.

## Proposed fix

Allow the `JsonSerializerOptions` used by the server session/transport for **protocol message**
serialization to be configured — e.g. have the session honor `McpServerOptions.JsonSerializerOptions`
(the property #925 introduces) for message writing as well, or add a transport-level option. Consumers
could then do:

```csharp
builder.Services.AddMcpServer(options =>
{
    options.JsonSerializerOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // or JavaScriptEncoder.Create(UnicodeRanges.All)
    };
});
```

This would let servers emit UTF-8 on the wire and dramatically reduce payload size for non-Latin
content, without changing the default (escaping) behavior for anyone who doesn't opt in.

## Environment

- `ModelContextProtocol` / `.Core` / `.AspNetCore` **1.4.0** (latest release)
- .NET 10
- Reproduced on both the stdio and HTTP transports

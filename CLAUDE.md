# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

An experimental Socket.IO client for .NET. The goal that shapes every design decision: expose the protocol through
Task-based async (`Task`, `IAsyncEnumerable`, `Channel<T>`) rather than the events-and-delegates style used by existing
.NET Socket.IO clients. Prefer an `await`-able or `await foreach`-able API over an `event` when adding surface area.

The Engine.IO layer is implemented (HTTP polling transport, WebSocket transport with upgrade, plain-text and binary
packets). The Socket.IO layer on top of it is half written: `src/SocketIO.Client` encodes packets and sends them, but
nothing parses an inbound one yet, so namespaces and acknowledgements are still open. See the TODOs in `README.md`.

Protocol references:
- Engine.IO: https://socket.io/docs/v4/engine-io-protocol
- Socket.IO: https://socket.io/docs/v4/socket-io-protocol

## Commands

```bash
dotnet build                       # build the whole solution
dotnet test                        # run all tests
dotnet format                      # apply .editorconfig formatting (scripts/dotnet-format.sh wraps this with -v d)

# a single test class or method (xUnit)
dotnet test --filter "FullyQualifiedName~PacketTests"
dotnet test --filter "FullyQualifiedName~PacketTests.PingProbePacket_Should_Be_Valid"
```

Running the sample end to end needs the Node test server, which lives in an npm workspace:

```bash
npm install                        # once, from the repo root
npm run start:server               # Engine.IO server on http://127.0.0.1:9854 (scripts/run-server.sh wraps this)
npm run start:socket-server        # Socket.IO server on http://127.0.0.1:9855
dotnet run --project samples/PingPong
```

`PingPong` hardcodes `http://127.0.0.1:9854` and only emits console logs in `Debug`; `Release` swaps in
`NullLoggerFactory`, so a Release run looks silent by design.

## Architecture

**`Engine` is a protocol state machine, not just a client wrapper.** `ConnectAsync` handshakes over HTTP polling first,
then — when `ClientOptions.AutoUpgrade` is set and the server advertises `websocket` in its handshake upgrades — builds a
`WebSocketTransport` and repoints `_transport` at it. Both transports are kept alive in fields because `Dispose` owns
them, but only the current `_transport` is read from. After connecting, `Engine` fires a detached `Task.Run(PollAsync)`
receive loop.

**`PollAsync` is where protocol concerns and consumer concerns split.** It parses every raw payload into a `Packet` and
routes by type: `Ping` is answered with a `Pong` inline (heartbeat never reaches the consumer), `Close` completes the
channel and disconnects, and only `Message` packets are written to `_packetsChannel`. `ListenAsync` drains that channel
as an `IAsyncEnumerable<Packet>`, linking the caller's token to the internal polling token so a transport failure ends
the consumer's `await foreach`. Anything you add that must not be visible to consumers belongs in this routing switch.

**`ITransport` is the seam.** Both transports return `ReadOnlyCollection<ReadOnlyMemory<byte>>` — a *collection* because
one HTTP poll response can carry several packets concatenated with the `0x1E` record separator, which
`HttpPollingTransport.GetAsync` splits. WebSocket frames are one packet each, so that transport returns a single-element
collection; the shared shape is what lets `Engine` treat them identically.

**Packets are wire-format-aware.** `Packet` is a `readonly struct` holding format, type and body, with `PacketType`
values being the *ASCII byte* of the digit (`Open = 0x30`, i.e. `'0'`) so parsing is a cast rather than a lookup.
`Packet` itself never produces bytes — the `ToPlaintextPacket()` / `ToBinaryPacket(IEncoder)` extensions in
`Transports/PacketExtensions.cs` do, prefixing the type byte (or `'b'` plus a base64 body for binary). Keep parsing in
`Packet.TryParse` and serialization in those extensions.

## Conventions

- `Directory.Build.props` sets solution-wide `Nullable=enable`, `ImplicitUsings=disable`, `LangVersion`, and a
  `Microsoft.Extensions.Logging.Abstractions` reference. Set properties there, not per-project, unless a project genuinely
  differs. Each project overrides `RootNamespace` for itself.
- **Every project targets `net10.0`.** The library was on `netstandard2.1` and is not any more, so the whole .NET 10 BCL
  is available in `src/` — but the existing code still reflects the old ceiling in places (hand-rolled byte copies in
  `PacketExtensions`, `Array.Empty<byte>()` over collection expressions). Don't take the current style as a constraint.
  Because everything is in the shared framework now, the library carries **no** `PackageReference` at all; adding
  `System.Text.Json` or `System.Threading.Channels` back would trip `NU1510`.
- `ImplicitUsings` is off, so every file carries explicit `using System;` etc.
- `.editorconfig` is authoritative and unusually strict: `end_of_line = crlf`, `insert_final_newline = false`,
  `var` is disallowed where the type is not apparent, and `using` groups are separated with `System` first. Run
  `dotnet format` rather than hand-matching it.
- `AssemblyInfo.cs` grants `InternalsVisibleTo("EngineIO.Client.Tests")`. Transports expose `internal` constructors that
  accept an injected `HttpClient` purely so tests can supply a mocked `HttpMessageHandler` — follow that pattern for new
  transports instead of adding public seams.
- Logging is optional throughout: `ILoggerFactory` is nullable everywhere and callers may pass nothing.

## Other agent configs present

The user has OpenAI Codex (`~/.codex`) and Gemini CLI (`~/.gemini`) configs on this machine. To bring over MCP servers,
slash commands, subagents, skills, or instructions from them, reply `/import` to scan and list what's importable, then
`/import --yes=<digest>` (the scan output names the digest) to apply the user-level items. If `/import` isn't available
on this surface, run `claude import` from a terminal.

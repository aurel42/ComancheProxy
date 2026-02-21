# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
make build    # dotnet build
make run      # dotnet run (normal mode)
make debug    # dotnet run -- --debug (verbose logging)
make clean    # dotnet clean
```

Target framework: .NET 10.0, C# 13 with nullable reference types and implicit usings.

## Architecture

ComancheProxy is a transparent TCP Man-in-the-Middle proxy for MSFS SimConnect. It sits between a SimConnect client (CLS2Sim force-feedback) on `127.0.0.1:5001` and the MSFS SimConnect server on `127.0.0.1:51111`.

**Default behavior:** pass-through relay of all SimConnect binary packets. When the A2A Comanche aircraft is detected, the proxy enters redirection mode — rewriting variable requests in-flight so the client unknowingly receives high-fidelity L-Var values instead of standard sim variables.

### Packet Flow

```
Client (CLS2Sim) ←→ ProxyBridge ←→ MSFS SimConnect Server
        :5001          ↕              :51111
                  StateTracker
                  TransformationEngine
```

### Key Components

- **ProxyBridge** — Bidirectional TCP relay. Runs two async pump loops (upstream→downstream, downstream→upstream). Inspects client packets for `AddToDataDefinition` and `RequestDataOnSimObject`; inspects sim packets for aircraft detection and `SIMOBJECT_DATA` response patching.
- **SimConnectFramer** (`Tcp/`) — Binary framing via `PipeReader`. Each SimConnect packet starts with a 4-byte `dwSize` header. Handles TCP fragmentation correctly. Validates packet bounds (12B min, 1MB max).
- **StateTracker** — Thread-safe state using `ConcurrentDictionary`. Maps DefineIDs to variable lists and RequestIDs to DefineIDs. Tracks `IsComancheMode` flag.
- **TransformationEngine** (`Redirection/`) — Normalizes L-Var responses (8-byte Float64) back to client-expected formats (4-byte Int32/Float32) by in-place buffer manipulation. Monitors autopilot state transitions.
- **VariableMapper** (`Redirection/`) — Dictionary of standard sim variable → L-Var mappings (e.g., `GENERAL ENG PCT MAX RPM:1` → `L:Eng1_RPM`).
- **StateTrackingModels** (`Models/`) — `VariableMetadata` record (name, unit, type, offset, size, redirection flag) and `DataDefinition` class with `ConcurrentQueue<VariableMetadata>`.
- **SimConnectConstants/Parser** (`Protocol/`) — Enums for RecvId, SendId, SimConnectDataType, plus binary parsing helpers using `BinaryPrimitives`.

### Binary Protocol

SimConnect packets have a 12-byte header: `dwSize` (4B) + `dwVersion` (4B) + `dwID` (4B). The proxy identifies packet types via `dwID` to decide when to intercept or transform.

The transformation sequence: intercept `AddToDataDefinition` → record variable definitions → intercept `RequestDataOnSimObject` → link request to definition → patch `RECV_SIMOBJECT_DATA` responses with L-Var values.

## Coding Standards (from .agent/rules/)

**Performance (critical path is latency-sensitive for force-feedback):**
- Zero-allocation: `Span<byte>`, `ReadOnlySpan<byte>` for packet parsing. `ArrayPool<byte>.Shared` for temp buffers. Avoid `Encoding.GetString()` in hot loops — use `BinaryPrimitives` and `Utf8Parser`.
- Avoid boxing: generics with `where T : struct`.
- Channels over queues: `System.Threading.Channels` (Unbounded or Bounded with DropOldest).
- NativeAOT compatible: no reflection or `Emit` (`PublishAot=true` target).

**Logging:**
- Use `[LoggerMessage]` source generators for all high-frequency log calls.
- Guard expensive serialization with `_logger.IsEnabled(LogLevel.Debug)`.

**Async patterns:**
- `ValueTask`/`ValueTask<T>` for methods that usually complete synchronously.
- Every async method accepts `CancellationToken`. Never `async void`.

**SimConnect specifics:**
- Struct alignment: `[StructLayout(LayoutKind.Sequential, Pack = 1)]`.
- Float64 variables at 8-byte aligned offsets within data definitions.
- `SafeHandle` for pipe/socket resources.

**Modern C# style:**
- File-scoped namespaces, primary constructors for DI, collection expressions `[1, 2, 3]`, switch expressions and pattern matching.
- XML docs on all public members.
- Physical quantity variable names include units (e.g., `airspeedKnots`, `altitudeFeet`).

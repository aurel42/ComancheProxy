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

ComancheProxy is a transparent TCP Man-in-the-Middle proxy for MSFS SimConnect. It listens on `127.0.0.1:5001` for the SimConnect client (CLS2Sim force-feedback) and relays traffic to the MSFS SimConnect server.

**Default behavior:** pass-through relay of all SimConnect binary packets. When the A2A Comanche aircraft is detected, the proxy enters redirection mode — injecting sidecar L-Var subscriptions into the MSFS TCP stream and overwriting client-facing data responses with high-fidelity values.

### Packet Flow

```
Client (CLS2Sim) ←→ ProxyBridge ←→ MSFS SimConnect Server
        :5001          ↕
                  StateTracker
                  TransformationEngine
                  SidecarInjector
```

### Key Components

- **ProxyBridge** — Bidirectional TCP relay. Runs two async pump loops (upstream→downstream, downstream→upstream). Inspects client packets for `AddToDataDefinition` and `RequestDataOnSimObject`; inspects sim packets for aircraft detection, sidecar response processing, and `SIMOBJECT_DATA` response patching. Disposes framers (`IAsyncDisposable`) and CTS on teardown; observes both pump tasks to prevent unobserved exceptions.
- **SimConnectFramer** (`Tcp/`) — Binary framing via `PipeReader`, implements `IAsyncDisposable`. Each SimConnect packet starts with a 4-byte `dwSize` header. Handles TCP fragmentation correctly. Validates packet bounds (12B min, 1MB max).
- **StateTracker** — Thread-safe state using `ConcurrentDictionary`. Maps DefineIDs to variable lists (`List<VariableMetadata>`) and RequestIDs to DefineIDs. Tracks `IsComancheMode` via `volatile` backing field. Trims variable names once at storage time.
- **TransformationEngine** (`Redirection/`) — Applies sidecar overrides (AP state, prop thrust) and elevator trim recentering to client data blocks. Writes values in the variable's native format (Int32/Float32/Float64) via `BinaryPrimitives`.
- **SidecarInjector** (`Redirection/`) — Injects `AddToDataDefinition` + `RequestDataOnSimObject` packets for A2A L-Vars (`ApDisableAileron`, `ApDisableElevator`, `Eng1_Thrust`) directly into the MSFS TCP stream. Intercepts and drops sidecar responses; provides override values to `TransformationEngine`.
- **StateTrackingModels** (`Models/`) — `VariableMetadata` record (name, unit, type, offset, size) and `DataDefinition` class with `List<VariableMetadata>`.
- **SimConnectConstants/Parser** (`Protocol/`) — Enums for RecvId, SendId, SimConnectDataType, plus binary parsing helpers using `BinaryPrimitives`.

### Binary Protocol

**Server→client** packets have a 12-byte header: `dwSize` (4B) + `dwVersion` (4B) + `dwID` (4B, bare RecvId).

**Client→server** packets have a 16-byte header: `dwSize` (4B) + `dwVersion` (4B) + `dwID` (4B, `0xF0000000 | SendId`) + `dwSendID` (4B, sequence counter). Payload fields start at offset 16, not 12. Use `dwId & 0xFF` to extract the SendId for matching.

The transformation sequence: intercept `AddToDataDefinition` → record variable definitions → intercept `RequestDataOnSimObject` → link request to definition → patch `RECV_SIMOBJECT_DATA` responses with L-Var values.

## Coding Rules

These rules are mandatory when generating or refactoring code for ComancheProxy.

### 1. Performance & Latency (The "Critical Path")

- **Zero-Allocation Buffers:** Use `Span<byte>` and `ReadOnlySpan<byte>` for all packet parsing logic. Use `ArrayPool<byte>.Shared` for temp buffers. Avoid `Encoding.ASCII.GetString()` on high-frequency loops; use `System.Buffers.Text.Utf8Parser` and `BinaryPrimitives` where possible.
- **Avoid Boxing:** Use generics with `where T : struct` for transformation math to prevent heap allocations.
- **Channels over Queues:** Use `System.Threading.Channels` (Unbounded or Bounded with DropOldest) for passing data between the SimConnect client and the Mock Server.
- **NativeAOT Compatibility:** Do not use heavy reflection or dynamic code generation (`Emit`). The project will be compiled with `PublishAot=true`.

### 2. High-Performance Logging

- **Source Generators:** Use `[LoggerMessage]` source generators for all high-frequency log calls (Trace/Debug) to avoid string interpolation and allocation when the log level is disabled.
- **Log Level Check:** Always check `_logger.IsEnabled(LogLevel.Debug)` before performing complex data serialization for logs.

### 3. Asynchronous Patterns

- **ValueTask:** Prefer `ValueTask` or `ValueTask<T>` for high-frequency methods that usually complete synchronously to reduce `Task` object overhead.
- **CancellationToken:** Every async method must accept and respect a `CancellationToken`.
- **No Async Void:** Never use `async void` except for top-level event handlers.

### 4. SimConnect Specifics

- **Struct Alignment:** All structs mapped to SimConnect data must use `[StructLayout(LayoutKind.Sequential, Pack = 1)]`.
- **Float64 Alignment:** Float64 variables at 8-byte aligned offsets within data definitions.
- **Safe Handles:** Use `SafeHandle` for pipe/socket management to ensure clean resource disposal during reconnection cycles.

### 5. Modern C# Syntax

- **File-scoped Namespaces:** Use `namespace MyProject;` to reduce indentation levels.
- **Primary Constructors:** Use primary constructors for dependency injection in services and managers.
- **Collection Expressions:** Use `[1, 2, 3]` syntax for array/list initialization.
- **Pattern Matching:** Favor switch expressions and property patterns for packet ID identification.

### 6. Documentation & Units

- **XML Documentation:** Every public member must have `<summary>` tags.
- **Units:** Variable names for physical quantities MUST include the unit (e.g., `airspeedKnots`, `altitudeFeet`, `deflectionRadians`).

# ComancheProxy — Design Document

This document describes the implemented architecture of ComancheProxy v0.1.6, a transparent TCP Man-in-the-Middle proxy for MSFS SimConnect.

## 1. Purpose

ComancheProxy sits between a SimConnect client (CLS2Sim force-feedback software) and the MSFS SimConnect server. In its default state, it is a transparent byte-for-byte relay. When the A2A Comanche aircraft is detected, the proxy enters **redirection mode** — injecting sidecar L-Var subscriptions into the MSFS stream and overwriting client-facing data responses with high-fidelity values the client never explicitly requested.

The client is unaware of the proxy's presence.

## 2. Environment

- **OS:** Windows 10/11
- **SDK:** .NET 10.0, C# 13
- **Nullable reference types** and **implicit usings** enabled
- **NativeAOT compatible** — no reflection or `Emit`

## 3. Network Topology

```
CLS2Sim client ──TCP──► 127.0.0.1:5001 (ProxyBridge listener)
                                │
                                ▼
                         ProxyBridge
                         ├── upstream pump   (client → sim)
                         └── downstream pump (sim → client)
                                │
                                ▼
                        MSFS SimConnect server
```

One `ProxyBridge` instance is created per client connection. When the client disconnects, all state is reset and the listener waits for a new connection.

## 4. Component Overview

### Source Layout

```
Program.cs                        Top-level entry point, DI, accept loop
ProxyBridge.cs                    Bidirectional TCP relay + packet inspection
StateTracker.cs                   Thread-safe definition/request/event state
ProxyLogger.cs                    [LoggerMessage] source-generated logger
Tcp/
  SimConnectFramer.cs             PipeReader-based binary framing
Protocol/
  SimConnectConstants.cs          RecvId, SendId, SimConnectDataType enums
  SimConnectParser.cs             Zero-allocation binary parsing helpers
Redirection/
  SidecarInjector.cs              L-Var packet injection + override values
  TransformationEngine.cs         In-place payload patching (overrides + trim)
  VariableMapper.cs               (empty — reserved for future mappings)
Models/
  StateTrackingModels.cs          VariableMetadata, DataDefinition, RequestMetadata
Logging/
  FileLoggerProvider.cs           NativeAOT-compatible file logger
```

### Component Responsibilities

**Program.cs** — Configures DI and logging, creates a `TcpListener` on port 5001, and runs the accept loop. Each accepted client triggers a `StateTracker.Reset()` + `SidecarInjector.Reset()` to clear stale state, then a new `ProxyBridge.RunAsync()` session.

**ProxyBridge** — Owns the bridge lifecycle. Creates two `SimConnectFramer` instances (one per direction) via `await using` for deterministic disposal. Launches two concurrent pump tasks:
- **Upstream pump** (`PumpUpstreamToDownstreamAsync`): reads client packets, calls `InspectClientPacket` to record definitions/requests, forwards to sim. A `SemaphoreSlim` serializes writes to the downstream socket (shared with sidecar injection).
- **Downstream pump** (`PumpDownstreamToUpstreamAsync`): reads sim packets, calls `InspectSimPacket` for aircraft detection, sidecar response processing, and payload patching, then forwards to client.

After `Task.WhenAny` completes, the bridge cancels the remaining pump via `CancellationTokenSource` and observes both tasks via `Task.WhenAll` to prevent unobserved exceptions. Both the CTS and the write semaphore are disposed in the `finally` block.

**SimConnectFramer** — Wraps a `NetworkStream` in a `PipeReader`. `ReadPacketAsync` peeks the first 4 bytes for `dwSize`, waits until the buffer contains the full packet, then returns the `ReadOnlySequence<byte>` without advancing. The caller processes the packet into an `ArrayPool<byte>` rental, then calls `AdvanceTo`. Implements `IAsyncDisposable` to call `_reader.CompleteAsync()` and release internal buffers on session teardown.

**StateTracker** — Thread-safe state store using `ConcurrentDictionary`:
- `_definitions`: maps `DefineID` → `DataDefinition` (list of `VariableMetadata`)
- `_requestToDefinition`: maps `RequestID` → `DefineID`
- `_eventMappings`: maps client event IDs to sim event names

Variable names and units are trimmed once at storage time (`AddVariableToDefinition`). `IsComancheMode` uses a `volatile` backing field for cross-thread visibility between the upstream and downstream pump threads.

**SidecarInjector** — Manages the proxy's own L-Var subscriptions. When the Comanche is detected, it builds and injects `AddToDataDefinition` packets for 3 L-Vars (`L:ApDisableAileron`, `L:ApDisableElevator`, `L:Eng1_Thrust`) plus a `RequestDataOnSimObject` packet, all using a high DefineID/RequestID (`0xFFFE0000`) to avoid collision with client-assigned IDs. Sidecar responses are intercepted, decoded, and dropped before reaching the client. Provides override values to `TransformationEngine` via `TryGetOverrideValue`.

**TransformationEngine** — Called on every `RECV_SIMOBJECT_DATA` response when Comanche mode is active. Iterates each variable in the data definition and:
1. Applies elevator trim recentering: symmetric linear scaling from raw center -0.36 to 0.0, clamped to [-1, +1].
2. Applies sidecar overrides: writes `AUTOPILOT MASTER` and `PROP THRUST:1` values in the variable's native format (Int32/Float32/Float64) via `BinaryPrimitives`.

**ProxyLogger** — All log methods use `[LoggerMessage]` source generators for zero-allocation logging. High-frequency Debug/Trace calls are gated behind `IsEnabled(LogLevel.Debug)`. A `FileLoggerProvider` writes Debug-level output to `logs/comanche-proxy.log` when `--debug` is passed.

## 5. Binary Protocol

### Server → Client Header (12 bytes)

| Offset | Type  | Field                    |
|--------|-------|--------------------------|
| 0      | DWORD | `dwSize`                 |
| 4      | DWORD | `dwVersion`              |
| 8      | DWORD | `dwID` (bare `RecvId`)   |

### Client → Server Header (16 bytes)

| Offset | Type  | Field                              |
|--------|-------|------------------------------------|
| 0      | DWORD | `dwSize`                           |
| 4      | DWORD | `dwVersion`                        |
| 8      | DWORD | `dwID` (`0xF0000000 \| SendId`)    |
| 12     | DWORD | `dwSendID` (sequence counter)      |

Extract `SendId` via `dwId & 0xFF`. Payload fields start at offset 16 for client→server, offset 12 for server→client.

### Framing

Packets are length-prefixed: `dwSize` (first 4 bytes) is the total packet length including the header. Minimum 12 bytes (server→client) or 16 bytes (client→server), maximum 1 MB. `SimConnectFramer` handles TCP fragmentation via `PipeReader` — it only yields a packet once the buffer contains `dwSize` bytes.

### Key Packet Types

| Direction | SendId/RecvId                 | Proxy Action                                                    |
|-----------|-------------------------------|-----------------------------------------------------------------|
| C→S       | `AddToDataDefinition` (12)    | Record variable metadata in `StateTracker`                      |
| C→S       | `RequestDataOnSimObject` (14) | Map RequestID → DefineID in `StateTracker`                      |
| C→S       | `MapClientEventToSimEvent` (8)| Record event mapping for debug logging                          |
| C→S       | `TransmitClientEvent` (9)     | Log event ID + data at Debug level                              |
| C→S       | `SetDataOnSimObject` (16)     | Log definition summary at Debug level                           |
| S→C       | `SimobjectData` (8/9)         | Patch payload with sidecar overrides + trim recentering         |
| S→C       | RecvId 5, 8, 15               | Scan for `aircraft.cfg` + `pa24-250` for Comanche detection     |
| S→C       | `Exception` (1)               | Log exception code, SendID, and index                           |

## 6. Transformation Sequence

```
1. Client sends AddToDataDefinition(DefineID=1, "ELEVATOR TRIM PCT", "percent", Float32)
   → StateTracker records: DefineID 1 → [ELEVATOR TRIM PCT @ offset 0, 4 bytes]

2. Client sends RequestDataOnSimObject(RequestID=100, DefineID=1)
   → StateTracker maps: RequestID 100 → DefineID 1

3. Sim sends RECV_SIMOBJECT_DATA(RequestID=100, payload=[...])
   → ProxyBridge looks up DefineID 1 via RequestID 100
   → TransformationEngine iterates variables:
     a. ELEVATOR TRIM PCT → recenter from -0.36 center point
     b. AUTOPILOT MASTER  → overwrite with sidecar aggregate (0.0 or 1.0)
     c. PROP THRUST:1     → overwrite with L:Eng1_Thrust sidecar value
   → Patched buffer forwarded to client
```

## 7. Sidecar Injection Lifecycle

```
 [Comanche detected]
        │
        ▼
 SidecarInjector.BuildInjectionPackets(dwVersion)
   → 3 × AddToDataDefinition (DefineID=0xFFFE0000, Float64)
       L:ApDisableAileron, L:ApDisableElevator, L:Eng1_Thrust
   → 1 × RequestDataOnSimObject (RequestID=0xFFFE0000, Period=VISUAL_FRAME)
        │
        ▼
 Written to MSFS stream under _downstreamWriteLock
        │
        ▼
 [Every VISUAL_FRAME tick]
   Sim sends RECV_SIMOBJECT_DATA(RequestID=0xFFFE0000)
   → SidecarInjector.ProcessResponse() decodes 3 Float64 values
   → Derives AP state: either ApDisableAileron or ApDisableElevator > 0.5
   → Packet dropped (never reaches client)
        │
        ▼
 [Non-Comanche aircraft detected]
   SidecarInjector.BuildCleanupPacket(dwVersion)
   → ClearDataDefinition(DefineID=0xFFFE0000)
```

## 8. Aircraft Detection

Zero-allocation binary scan — no string conversion. `InspectSimPacket` checks RecvId 5 (`EventObjectAddremove`), 8 (`SimobjectData`), and 15 (`SystemState`) for:
1. The ASCII subsequence `aircraft.cfg` (case-insensitive)
2. If found, the ASCII subsequence `pa24-250` (case-insensitive)

This fires on aircraft load/change packets. On transition, `StateTracker.IsComancheMode` is set and sidecar injection/cleanup is triggered.

## 9. Session Lifecycle

```
Program.cs accept loop
  │
  ▼
AcceptTcpClientAsync() → new client
  │
  ├── StateTracker.Reset()
  ├── SidecarInjector.Reset()
  │
  ├── Connect to MSFS SimConnect server
  │
  └── ProxyBridge.RunAsync()
        ├── await using upstreamFramer
        ├── await using downstreamFramer
        ├── Task upstream pump
        ├── Task downstream pump
        ├── WhenAny → log which side closed
        ├── Cancel + WhenAll → observe both tasks
        └── finally: CTS.Dispose(), SemaphoreSlim.Dispose()
  │
  ▼
Loop back to AcceptTcpClientAsync()
```

## 10. Performance Constraints

CLS2Sim polls at ~60 Hz. GC pauses are perceptible as force-feedback hitches.

| Constraint | Implementation |
|------------|----------------|
| Zero-allocation hot path | `ArrayPool<byte>.Shared` rentals, `Span<byte>` / `ReadOnlySpan<byte>` for all parsing |
| No string allocation on data path | Variable names trimmed once at storage time; `BinaryPrimitives` for all reads/writes |
| No boxing | Generics with `where T : struct` for transformation math |
| Source-generated logging | `[LoggerMessage]` on all methods; Debug calls gated behind `IsEnabled` |
| Deterministic resource cleanup | `SimConnectFramer` implements `IAsyncDisposable`; `CancellationTokenSource` disposed after cancel |
| Thread safety | `ConcurrentDictionary` for state; `volatile` backing field for `IsComancheMode`; `SemaphoreSlim` for downstream write serialization |
| List over ConcurrentQueue | `DataDefinition.Variables` uses `List<T>` — populated during setup, iterated on every packet at 60 Hz |

## 11. Variable Override Mappings

| Client Variable        | Override Source                                   | Method           |
|------------------------|---------------------------------------------------|------------------|
| `AUTOPILOT MASTER`     | `L:ApDisableAileron` OR `L:ApDisableElevator` > 0.5 → 1.0 | Sidecar override |
| `PROP THRUST:1`        | `L:Eng1_Thrust`                                   | Sidecar override |
| `ELEVATOR TRIM PCT`    | Raw value recentered: `(raw − (−0.36)) / 0.64`, clamped [-1, +1] | In-place transform |

## 12. CLI

```
dotnet run              # Normal mode (Information level console)
dotnet run -- --debug   # Debug mode (Debug level file logging to logs/comanche-proxy.log)
```

Or via Makefile: `make run`, `make debug`, `make build`, `make clean`.

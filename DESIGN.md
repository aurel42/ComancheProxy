# MSFS Comanche SimConnect Proxy - Full Specification

This document defines the hardened architecture, binary protocol implementation, and defensive coding requirements for the Comanche SimConnect Proxy.

## 1. Prerequisites & Environment

### Environment Setup
- **OS:** Windows 10/11
- **SDK:** .NET 10.0 (LTS)
- **IDE:** Antigravity IDE (v2.4+)
- **Terminal:** PowerShell and Bash support via Integrated Terminal.

### MSFS SDK Requirements
- **SimConnect Core:** MSFS SDK installed.
- **WASM Bridge:** A module (e.g., MobiFlight or custom) Exposure of $L$-Variables via dedicated Client Data Areas.

## 2. Architectural Overview

The proxy operates as a high-fidelity "Man-in-the-Middle" (MitM) bridge over TCP. It provides transparent pass-through by default and enters a redirection state when the A2A Comanche is detected.

### Connectivity Topology
- **Upstream (Client-Facing):** TCP Listener on `127.0.0.1:5001`.
- **Downstream (Sim-Facing):** TCP Client connecting to MSFS (default `127.0.0.1:51111`).
- **L-Var Plane:** A background SimConnect client polling the WASM bridge for specific aircraft states.

## 3. Implementation Details: Addressing the "Junior Traps"

### 3.1 Robust TCP Framing (`System.IO.Pipelines`)
TCP is a byte-stream, not a packet-stream.
- **The Protocol Rule:** Every SimConnect packet starts with a 4-byte `dwSize`.
- **Implementation:** Use a `PipeReader` to consume bytes.
  - Peek the first 4 bytes.
  - Wait until the `PipeReader` buffer contains at least `dwSize` bytes.
  - Only then "Slice" the `ReadOnlySequence<byte>` for processing.
  - This prevents corrupted state on fragmented packets.

### 3.2 Memory Alignment & Struct Layout
- **The Protocol Rule:** SimConnect managed structs must use `[StructLayout(LayoutKind.Sequential, Pack = 1)]` as per project rules.
- **Alignment Logic:** While the struct pack is 1, `SIMCONNECT_DATATYPE_FLOAT64` variables should still be placed at 8-byte aligned offsets within the custom data definition to avoid server-side errors.
- **Defensive Structs:** Use primary constructors for dependency injection in all managers and services.

### 3.3 Zero-Allocation Transformation (`Span<T>`)
High-frequency force-feedback (CLS2Sim) is extremely sensitive to Garbage Collection (GC) pauses.
- **Implementation:** 
  - Use `ReadOnlySpan<byte>` for inspection and `Span<byte>` for modification.
  - Avoid `BitConverter` and `Encoding.GetString()`. Use `System.Buffers.Binary.BinaryPrimitives` and `System.Buffers.Text.Utf8Parser`.
  - Use `ArrayPool<byte>.Shared` or `System.Threading.Channels` (DropOldest) for transient data passing.
- **NativeAOT**: Ensure no reflection or `Emit` is used to maintain `PublishAot=true` compatibility.


### 3.4 Concurrency & State Management
The proxy manages two asynchronous loops: **Upstream-to-Downstream** and **Downstream-to-Upstream**.
- **State Store:** Use a thread-safe `ConcurrentDictionary<uint, DataDefinitionMetadata>` to map `DefineID`s to their high-fidelity redirection rules.
- **Race Condition Guard:** The `isComancheMode` flag must be checked atomically during the `dwID` evaluation phase of every packet.

## 4. Binary Protocol Anatomy

### Header: `SIMCONNECT_RECV` (12 bytes)
| Offset | Type | Field |
| :--- | :--- | :--- |
| 0 | DWORD | `dwSize` |
| 4 | DWORD | `dwVersion` |
| 8 | DWORD | `dwID` |

### Transformation Sequence
1. **Intercept `AddToDataDefinition`**: Record the relationship between `DefineID`, the variable string, and its unit.
2. **Intercept `RequestDataOnSimObject`**: Link the `RequestID` to the specific definition.
3. **Patch `RECV_SIMOBJECT_DATA`**: 
   - Identify the `DefineID` at the packet's payload offset.
   - If matched, calculate the variable offset within the payload.
   - Patch the bytes with the high-fidelity A2A value.

## 5. Implementation Roadmap

The implementation is divided into four sequential phases. For granular technical steps, see the [Implementation Plan](file:///C:/Users/aurel/.gemini/antigravity/brain/fdef3ea8-b58b-48a0-9a9c-f3d84a20a0c2/implementation_plan.md).

### Phase 1: The Raw Bridge (MVP)
Establish solid TCP connectivity and a high-performance framing reactor.

### Phase 2: Observation & State Tracking
Intercept definitions and detect the active aircraft to determine when to trigger the redirection engine.

### Phase 3: The Redirection Core
Integrate the WASM bridge for L-Var resolution and implement binary payload patching.

### Phase 4: Hardening & Performance
Apply 64-bit alignment guards, refactor for zero-allocation performance, and implement session resilience.

## 6. Target Variable Mappings

| Feature | Standard Sim Variable | Accu-Sim / L-Var Source | Format |
| :--- | :--- | :--- | :--- |
| **Engine RPM** | `GENERAL ENG RPM:1` | `L:Eng1_RPM` | Float64 |
| **Airspeed** | `AIRSPEED INDICATED` | `L:AirspeedIndicated` | Float64 |
| **AP State** | `AUTOPILOT MASTER` | `L:ApMaster` | Int32 (0/1) |
| **Prop Thrust** | `PROPELLER THRUST:1` | `L:Eng1_Thrust` | Float64 |

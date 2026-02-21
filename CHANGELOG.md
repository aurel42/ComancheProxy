# Changelog

## [0.1.6] - 2026-02-21

### Changed
- Rewrote `DESIGN.md` to match the implemented architecture — removed stale roadmap, WASM bridge references, and old AP detection scheme; added component responsibility map, sidecar lifecycle, session lifecycle, performance constraints table, and transformation sequence walkthrough

### Fixed
- Corrected erroneous WASM bridge reference in `README.md` — proxy uses sidecar TCP injection, not a WASM bridge
- Removed hardcoded MSFS port numbers from all documentation — only the listen port (`5001`) is documented
- Updated `CLAUDE.md` component descriptions to reflect current implementation (sidecar injector, volatile IsComancheMode, List over ConcurrentQueue, IAsyncDisposable framers)

## [0.1.5] - 2026-02-21

### Fixed
- PipeReader leak: `SimConnectFramer` now implements `IAsyncDisposable` and completes the reader on disposal — buffers no longer accumulate across reconnections
- CancellationTokenSource leak: `ProxyBridge` now disposes `_cts` in the `finally` block
- Unobserved Task exception: after `WhenAny`, both pump tasks are now observed via `WhenAll` to prevent process crash from `TaskScheduler.UnobservedTaskException`
- Hot-path allocation: moved `string.Trim()` from `TransformationEngine.NormalizePayload` (called at ~60 Hz) to one-time storage in `StateTracker.AddVariableToDefinition`
- `IsComancheMode` thread visibility: backed by `volatile` field for correct cross-thread reads

### Changed
- `DataDefinition.Variables` changed from `ConcurrentQueue<VariableMetadata>` to `List<VariableMetadata>` — eliminates unnecessary thread-safety overhead on the hot-path iteration in `NormalizePayload`

## [0.1.4] - 2026-02-21

### Added
- Elevator trim recentering: symmetric linear scaling recenters `ELEVATOR TRIM PCT` from -0.36 → 0.0 with ±0.64 half-range, clamped to [-1, +1] — fixes CLS2Sim force-feedback center offset on the A2A Comanche
- Client packet inspection: decode `MapClientEventToSimEvent`, `TransmitClientEvent`, and `SetDataOnSimObject` at Debug level
- Debug file logging: `--debug` writes Debug-level output to `logs/comanche-proxy.log` while console stays at Information level
- Session state reset on reconnection: `StateTracker.Reset()` and `SidecarInjector.Reset()` clear stale state between bridge sessions

### Changed
- AP detection rewritten: replaced 5 status-light L-Vars (`ApStLight`, `ApHdLight`, `ApTrkHiLight`, `ApTrkLoLight`, `ApAltLight`) with 2 axis-disable L-Vars (`ApDisableAileron`, `ApDisableElevator`) for reliable autopilot state detection
- Upstream pump now supports packet suppression (`InspectClientPacket` returns `bool`)

## [0.1.2] - 2026-02-21

### Added
- Sidecar L-Var injection: `SidecarInjector` injects `AddToDataDefinition` + `RequestDataOnSimObject` packets to subscribe to A2A Comanche L-Vars directly via the MSFS TCP stream
- Autopilot state override: aggregates 5 AP status-light L-Vars (`ApStLight`, `ApHdLight`, `ApTrkHiLight`, `ApTrkLoLight`, `ApAltLight`) into `AUTOPILOT MASTER`
- Prop thrust override: replaces `PROP THRUST:1` with `L:Eng1_Thrust` sidecar value
- `TransformationEngine` applies sidecar overrides to client data blocks (Int32/Float32/Float64 format-aware)
- `SendIdExtensions.Wire()` for constructing client→server wire-format packet IDs (`0xF0000000 | SendId`)
- Sidecar logging: AP state transitions, injection/cleanup events, debug-level value dumps

### Fixed
- SimConnect client→server protocol: packets use 16-byte header with `0xF0000000 | SendId` prefix and `dwSendID` sequence field — fixed `InspectClientPacket` to mask with `dwId & 0xFF`
- Corrected `DefineID` offset from 12→16 in `AddToDataDefinition` parsing
- Corrected `RequestID`/`DefineID` offsets from 12/16→16/20 in `RequestDataOnSimObject` parsing
- Sidecar re-injection on CLS2Sim reconnection: decoupled from state-change detection, now fires on every new bridge where Comanche is active
- `RunAsync` now surfaces faulted pump tasks instead of silently reporting "connection closed"

### Changed
- `VariableMapper` emptied — all redirections now handled via sidecar injection instead of variable name rewriting

## [0.1.1] - 2026-02-21

- Implement native in-flight packet redirection: rewrite `AddToDataDefinition` packets in-place, swapping sim variable names with L-Var names
- Add `TransformationEngine.NormalizePayload` to cast 8-byte L-Var responses back to 4-byte client-expected types with buffer shifting
- Add zero-allocation aircraft detection via binary scan for `pa24-250` in `aircraft.cfg` paths
- Add `--debug` CLI flag and structured log messages for all subsystems
- Correct `SendId` values (`AddToDataDefinition=12`, `RequestDataOnSimObject=14`) confirmed by hex analysis
- Fix variable mappings: RPM key, AP L-Var name; add Prop Thrust mapping
- Remove `LVarProvider.cs` and `StateTracker.UpdateAircraftTitle` (replaced by native packet rewriting and binary scan)
- Both pump loops now use `ArrayPool<byte>` + `Span<byte>` instead of `ReadOnlySequence` segments

## [0.1.0] - 2026-02-07

- Initial release: transparent SimConnect TCP proxy with state tracking
- Bidirectional TCP bridge (`ProxyBridge`) between client (port 5001) and MSFS SimConnect server
- Binary packet framing via `PipeReader` with TCP fragmentation handling
- State tracking of `AddToDataDefinition` and `RequestDataOnSimObject` calls
- Aircraft title detection for Comanche mode activation
- Variable mapping dictionary and mock L-Var provider for testing
- High-performance logging with `[LoggerMessage]` source generators
- Dependency injection, graceful Ctrl+C shutdown, .NET 10.0 / C# 13

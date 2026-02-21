# Changelog

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

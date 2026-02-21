# ComancheProxy

ComancheProxy is a technical Man-in-the-Middle (MitM) bridge for MSFS SimConnect, implemented as a bidirectional TCP relay. It facilitates transparent pass-through of SimConnect binary packets while providing real-time redirection and patching for specific aircraft profiles, specifically the A2A Comanche.

## Architecture

The proxy operates between a SimConnect client (such as CLS2Sim) and the MSFS SimConnect server. It executes two asynchronous pump loops to handle upstream (client-facing) and downstream (sim-facing) traffic.

### Connectivity
- **Client Interface (Upstream):** TCP Listener on `127.0.0.1:5001`.
- **MSFS Interface (Downstream):** TCP Client connecting to the MSFS SimConnect server.

## Technical Implementation

### Binary Redirection
When the A2A Comanche is detected, the proxy enters redirection mode. It injects sidecar `AddToDataDefinition` + `RequestDataOnSimObject` packets directly into the MSFS TCP stream to subscribe to A2A L-Vars, while intercepting the client's own packets to map variable offsets. Incoming `RECV_SIMOBJECT_DATA` responses are patched in-flight, replacing standard simulator variables with high-fidelity L-Var values from the sidecar subscription.

### Performance Optimizations
- **Zero-Allocation Parsing:** Utilizes `Span<byte>` and `ReadOnlySpan<byte>` for packet inspection and manipulation.
- **Robust Framing:** Employs `System.IO.Pipelines` for high-performance TCP stream framing, ensuring correct handling of fragmented packets.
- **Low Latency:** Optimized for high-frequency force-feedback applications where garbage collection pauses must be minimized.
- **NativeAOT Compatibility:** Compiled with `PublishAot=true`, avoiding reflection and dynamic code generation.

## Development

### Prerequisites
- .NET 10.0 SDK
- MSFS SDK (for SimConnect headers/definitions)

### Build Commands
The project includes a `Makefile` for common development tasks:

```bash
make build    # build the project
make run      # execute in normal mode
make debug    # execute with verbose logging
make clean    # clean build artifacts
```

## Internal Components

- **ProxyBridge:** Core bidirectional relay logic.
- **SimConnectFramer:** Binary framing reactor using `PipeReader`.
- **StateTracker:** Thread-safe management of definition mappings and aircraft state.
- **TransformationEngine:** In-place buffer manipulation for variable patching.

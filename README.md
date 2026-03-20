# ComancheProxy

ComancheProxy is a Man-in-the-Middle (MitM) bridge for MSFS SimConnect, implemented as a bidirectional TCP relay. It facilitates transparent pass-through of SimConnect binary packets while providing real-time patching for specific aircraft profiles, specifically the A2A Comanche.

## Installation
- Unpack the release archive to a location of your choosing
- Make sure you have the binary (ComancheProxy.exe) and the config file (config.json) in the same directory

## Verification
- Run MSFS and CLS2Sim: CLS2Sim should continue to connect to the simulator as before, but it should say "Connected to Microsoft Flight Simulator X" now.
- Run `ComancheProxy.exe`, you should see a terminal window with status messages.
- Force a reconnect of CLS2Sim, for example by restarting the program or by starting the Profile Manager (you can use Cancel immediately to close the Profile Manager window).
- When CLS2Sim connects to ComancheProxy, you should see messages in the ComancheProxy window.
- Load the A2A Comanche in MSFS. You should see a message in the ComancheProxy window indicating that the A2A Comanche has been detected.

## Usage
- You should be able to start ComancheProxy, CLS2Sim, and MSFS in any order.
- If you start CLS2Sim before ComancheProxy, you will have to force a reconnect (see above).
- If you start ComancheProxy first (recommended), it should automatically connect to MSFS when it starts, and CLS2Sim should automatically connect to ComancheProxy.
- FSEconomy: configure the FSEconomy SimConnect client to use port 5002 (different port!) on `127.0.0.1`. Start MSFS and ComancheProxy first, then start the FSE client.

## Configuration:
- `config.json` is configured to handle the A2A Comanche for CLS2Sim and to "convert" the AeroElvira Optica (the one that comes with MSFS) into a C172 for FSEconomy.
- If you don't use one or the other, just ignore the config entries (or remove them, up to you).

## Architecture
The proxy operates between a SimConnect client (such as CLS2Sim) and the MSFS SimConnect server. It executes two asynchronous pump loops to handle upstream (client-facing) and downstream (sim-facing) traffic.

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

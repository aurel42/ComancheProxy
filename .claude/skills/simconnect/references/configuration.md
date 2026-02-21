# SimConnect Configuration Reference

Source: MSFS 2024 SDK `SimConnect_CFG_Definition.htm`.

## SimConnect.cfg

Client-side configuration file for remote SimConnect connections. Place in the same directory as the client application. **Not required for local connections** — use `SIMCONNECT_OPEN_CONFIGINDEX_LOCAL` (-1) instead.

### File Format

```ini
[SimConnect.0]
Protocol=Pipe
Port=Custom/SimConnect
Address=127.0.0.1

[SimConnect.1]
Protocol=IPv4
Port=51111
Address=127.0.0.1

; Additional connections as [SimConnect.N]
```

### Section Naming

- `[SimConnect]` — equivalent to `[SimConnect.0]` (default config)
- `[SimConnect.N]` — numbered configs (0-based), selected via `ConfigIndex` parameter in `SimConnect_Open`

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| Protocol | String | IPv4 | `Pipe`, `IPv4`, or `IPv6`. Fallback order: Pipe → IPv6 → IPv4 → Pipe |
| Address | String | — | Server hostname or IP address |
| Port | String | — | Port number (TCP) or pipe name |
| MaxReceiveSize | Integer | 8192 | Max packet size in bytes. Client disconnects if exceeded |
| DisableNagle | Bool | 0 | Set to 1 to disable Nagle's algorithm (reduces latency, increases packets) |

### Protocol Details

**Pipe** (local only):
- Named pipe communication
- Avoids firewall/antivirus interference
- Pipe name typically: `Custom/SimConnect`

**IPv4 / IPv6** (local or remote):
- Standard TCP socket connection
- IPv6 offers better security features
- Default port varies by MSFS installation (check `SimConnect.xml` on the server)

### SimConnect_Open ConfigIndex

```c
SimConnect_Open(&hSimConnect, "AppName", NULL, 0, 0, ConfigIndex);
```

| ConfigIndex | Behavior |
|-------------|----------|
| 0 | Uses `[SimConnect]` or `[SimConnect.0]` section |
| N | Uses `[SimConnect.N]` section |
| -1 (0xFFFFFFFF) | `SIMCONNECT_OPEN_CONFIGINDEX_LOCAL` — ignores SimConnect.cfg, forces local pipe connection |

### SimConnect.xml (Server-Side)

Located in the MSFS installation directory. Defines which protocols and ports the **server** listens on. Clients must match these settings in their `SimConnect.cfg`.

### ComancheProxy Configuration

The proxy uses hardcoded TCP connections (no SimConnect.cfg):
- Listens for CLS2Sim client on `127.0.0.1:5001`
- Connects to MSFS SimConnect server on `127.0.0.1:51111`
- Protocol: IPv4 TCP (no pipe, no SimConnect.cfg)

# SimConnect Binary Protocol — Packet Layouts

All offsets in bytes from packet start. All multi-byte fields are **little-endian**. Structs are `Pack=1` (no padding between fields).

Source: `SimConnect.h` structs + ComancheProxy verified byte offsets.

---

## Common Header: SIMCONNECT_RECV

**Server→client** packets use a 12-byte header:

```
Offset  Size  Field       Description
------  ----  ----------  ------------------------------------------
0       4     dwSize      Total packet size in bytes (including header)
4       4     dwVersion   Protocol version
8       4     dwID        RecvId (bare enum value)
```

**Client→server** packets use a 16-byte header with a `0xF0000000` prefix on dwID and an extra sequence field:

```
Offset  Size  Field       Description
------  ----  ----------  ------------------------------------------
0       4     dwSize      Total packet size in bytes (including header)
4       4     dwVersion   Protocol version
8       4     dwID        0xF0000000 | SendId
12      4     dwSendID    Packet sequence counter
```

Constraints: `dwSize >= 12` (server→client), `dwSize >= 16` (client→server), max 1,048,576 (1 MB).

**Important:** To match SendId from dwID on client→server packets, mask with `dwId & 0xFF`.

---

## Server → Client Packets

### RECV_EXCEPTION (RecvId 1)

Returned when a client request causes an error.

```
Offset  Size  Field         Description
------  ----  -----------   ------------------------------------------
0       12    [header]      dwID = 1
12      4     dwException   SIMCONNECT_EXCEPTION enum value
16      4     dwSendID      Packet ID of the request that caused the error (0 = unknown)
20      4     dwIndex       Parameter index that was the error source (0xFFFFFFFF = unknown)
```

Total: 24 bytes.

### RECV_OPEN (RecvId 2)

Returned after successful `SimConnect_Open`. Contains version information.

```
Offset  Size  Field                       Description
------  ----  --------------------------  ------------------------------------------
0       12    [header]                    dwID = 2
12      256   szApplicationName           Null-terminated ASCII, zero-filled
268     4     dwApplicationVersionMajor
272     4     dwApplicationVersionMinor
276     4     dwApplicationBuildMajor
280     4     dwApplicationBuildMinor
284     4     dwSimConnectVersionMajor
288     4     dwSimConnectVersionMinor
292     4     dwSimConnectBuildMajor
296     4     dwSimConnectBuildMinor
300     4     dwReserved1
304     4     dwReserved2
```

Total: 308 bytes.

### RECV_SIMOBJECT_DATA (RecvId 8) / RECV_SIMOBJECT_DATA_BYTYPE (RecvId 9)

**Primary packet the proxy intercepts and rewrites.**

```
Offset  Size  Field           Description
------  ----  -------------   ------------------------------------------
0       12    [header]        dwID = 8 or 9
12      4     dwRequestID     Client-assigned request ID
16      4     dwObjectID      Sim object ID (0 = user aircraft)
20      4     dwDefineID      Data definition ID
24      4     dwFlags         SIMCONNECT_DATA_REQUEST_FLAG
28      4     dwEntryNumber   Entry N of M (1-based) for multi-object results
32      4     dwOutOf         Total entries M
36      4     dwDefineCount   Number of data items (not byte count)
40      var   dwData          Payload — contiguous data fields per definition
```

Minimum header: 40 bytes. Payload starts at offset 40.

**Payload format (non-tagged, dwFlags & 0x02 == 0):**
Data items packed contiguously in the order they were added via `AddToDataDefinition`. Each item occupies the byte size of its declared `SIMCONNECT_DATATYPE`.

**Payload format (tagged, dwFlags & 0x02 != 0):**
Each item is prefixed by a DWORD datum ID:
```
[DWORD DatumID][value bytes][DWORD DatumID][value bytes]...
```

**RECV_CLIENT_DATA (RecvId 16)** uses the identical struct layout.

### RECV_EVENT (RecvId 4)

```
Offset  Size  Field       Description
------  ----  ----------  ------------------------------------------
0       12    [header]    dwID = 4
12      4     uGroupID    Notification group (0xFFFFFFFF = unknown)
16      4     uEventID    Client event ID
20      4     dwData      Event-specific context data
```

Total: 24 bytes.

### RECV_EVENT_FILENAME (RecvId 6)

Extends RECV_EVENT. Used for aircraft loading — proxy uses this for Comanche detection.

```
Offset  Size  Field       Description
------  ----  ----------  ------------------------------------------
0       24    [event]     Base RECV_EVENT struct
24      260   szFileName  MAX_PATH null-terminated ASCII
284     4     dwFlags
```

Total: 288 bytes.

### RECV_SYSTEM_STATE (RecvId 15)

```
Offset  Size  Field       Description
------  ----  ----------  ------------------------------------------
0       12    [header]    dwID = 15
12      4     dwRequestID
16      4     dwInteger
20      4     fFloat      (IEEE 754 float)
24      260   szString    MAX_PATH null-terminated ASCII
```

Total: 284 bytes.

---

## Client → Server Packets

Client packets have a 16-byte header: 12 standard bytes + `dwSendID` sequence field at offset 12. `dwID` contains `0xF0000000 | SendId`.

### AddToDataDefinition (SendId 12)

**Proxy-critical.** Registers a variable within a data definition. The proxy rewrites the name/units fields to redirect standard variables to L-Vars.

```
Offset  Size  Field       Description
------  ----  ----------  ------------------------------------------
0       16    [header]    dwID = 0xF000000C, dwSendID = sequence
16      4     DefineID    Data definition group ID
20      256   DatumName   Variable name, null-terminated ASCII, zero-filled
276     256   UnitsName   Unit string, null-terminated ASCII, zero-filled
532     4     DatumType   SIMCONNECT_DATATYPE enum value
536     4     fEpsilon    Change threshold (IEEE 754 float)
540     4     DatumID     User-assigned datum ID (0xFFFFFFFF = UNUSED)
```

Minimum for proxy inspection: 536 bytes (`buffer.Length >= 536`).

**Proxy rewrite targets:** DatumName (@20, 256B) and UnitsName (@276, 256B) are overwritten in-place when redirecting to L-Vars.

### RequestDataOnSimObject (SendId 14)

**Proxy-critical.** Links a request ID to a definition ID for response tracking.

```
Offset  Size  Field       Description
------  ----  ----------  ------------------------------------------
0       16    [header]    dwID = 0xF000000E, dwSendID = sequence
16      4     RequestID   Client-assigned request ID
20      4     DefineID    Data definition to request
24      4     ObjectID    Target object (0 = user aircraft)
28      4     Period      SIMCONNECT_PERIOD enum
32      4     Flags       SIMCONNECT_DATA_REQUEST_FLAG
36      4     origin      Number of periods to skip before starting
40      4     interval    Periods between sends (0 = every period)
44      4     limit       Max number of sends (0 = unlimited)
```

Total: 48 bytes.

### ClearDataDefinition (SendId 13)

Removes all variables from a data definition.

```
Offset  Size  Field       Description
------  ----  ----------  ------------------------------------------
0       16    [header]    dwID = 0xF000000D, dwSendID = sequence
16      4     DefineID    Definition to clear
```

Total: 20 bytes.

### Known SendId Values

These are confirmed from the proxy codebase and protocol analysis:

| SendId | Function |
|--------|----------|
| 1 | SimConnect_Open |
| 2 | SimConnect_Close |
| 4 | SubscribeToSystemEvent |
| 5 | UnsubscribeFromSystemEvent |
| 8 | MapClientEventToSimEvent |
| 9 | TransmitClientEvent |
| 10 | SetSystemEventState |
| 12 | **AddToDataDefinition** |
| 13 | ClearDataDefinition |
| 14 | **RequestDataOnSimObject** |

Note: SendId values are internal to the wire protocol and not officially documented by Microsoft. The values above are the low-byte values confirmed via hex analysis. On the wire, dwID is `0xF0000000 | SendId` — use `dwId & 0xFF` to extract the SendId for matching.

---

## Complex Data Structures

### SIMCONNECT_DATA_INITPOSITION (DATATYPE 12)

```
Offset  Size  Field      Type     Description
------  ----  ---------  -------  -----------
0       8     Latitude   double   Degrees
8       8     Longitude  double   Degrees
16      8     Altitude   double   Feet
24      8     Pitch      double   Degrees
32      8     Bank       double   Degrees
40      8     Heading    double   Degrees
48      4     OnGround   DWORD    1 = force on ground
52      4     Airspeed   DWORD    Knots (-1 = cruise, -2 = keep)
```

Total: 56 bytes.

### SIMCONNECT_DATA_LATLONALT (DATATYPE 15)

```
Offset  Size  Field      Type     Description
------  ----  ---------  -------  -----------
0       8     Latitude   double   Degrees
8       8     Longitude  double   Degrees
16      8     Altitude   double   Meters
```

Total: 24 bytes.

### SIMCONNECT_DATA_XYZ (DATATYPE 16)

```
Offset  Size  Field  Type     Description
------  ----  -----  -------  -----------
0       8     x      double
8       8     y      double
16      8     z      double
```

Total: 24 bytes.

### SIMCONNECT_DATA_WAYPOINT (DATATYPE 14)

```
Offset  Size  Field             Type           Description
------  ----  ----------------  -------------  -----------
0       8     Latitude          double         Degrees
8       8     Longitude         double         Degrees
16      8     Altitude          double         Feet
24      4     Flags             unsigned long  SIMCONNECT_WAYPOINT_FLAGS
28      8     ktsSpeed          double         Knots
36      8     percentThrottle   double         0-100
```

Total: 44 bytes.

### SIMCONNECT_DATA_MARKERSTATE (DATATYPE 13)

```
Offset  Size  Field          Type    Description
------  ----  -------------  ------  -----------
0       64    szMarkerName   char[]  Null-terminated ASCII
64      4     dwMarkerState  DWORD
```

Total: 68 bytes.

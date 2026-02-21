---
name: simconnect
description: >
  SimConnect binary protocol reference for MSFS 2024. Use when working on
  SimConnect TCP proxy code, binary packet parsing/rewriting, data definition
  interception, or any code that touches SimConnect packet structures, enums,
  or data types.
---

# SimConnect Protocol Quick-Reference

## 1. Packet Framing

**Server→client** packets have a 12-byte header:
```
[dwSize: 4B][dwVersion: 4B][dwID: 4B][...payload...]
```

**Client→server** packets have a 16-byte header with an extra `dwSendID` sequence field and a `0xF0000000` prefix on `dwID`:
```
[dwSize: 4B][dwVersion: 4B][dwID: 4B (0xF0000000|SendId)][dwSendID: 4B][...payload...]
```

- **Byte order:** Little-endian throughout
- **dwSize:** Total packet length including header (min 12 server→client, min 16 client→server; max 1,048,576)
- **dwID:** `RecvId` for server→client (bare values), `SendId` for client→server (OR'd with `0xF0000000`)
- **dwSendID:** Client→server only — packet sequence counter at offset 12
- **Framing:** TCP stream; read `dwSize` from first 4 bytes, then read remaining `dwSize - 4` bytes
- **Strings:** Null-terminated ASCII in fixed-width fields, zero-filled to slot size

## 2. Data Types

| Enum | Name | Bytes | Alignment |
|------|------|-------|-----------|
| 0 | INVALID | 0 | — |
| 1 | INT32 | 4 | 4 |
| 2 | INT64 | 8 | 8 |
| 3 | FLOAT32 | 4 | 4 |
| 4 | FLOAT64 | 8 | 8 |
| 5 | STRING8 | 8 | 1 |
| 6 | STRING32 | 32 | 1 |
| 7 | STRING64 | 64 | 1 |
| 8 | STRING128 | 128 | 1 |
| 9 | STRING256 | 256 | 1 |
| 10 | STRING260 | 260 | 1 |
| 11 | STRINGV | var | 1 |
| 12 | INITPOSITION | 56 | 8 |
| 13 | MARKERSTATE | 68 | 1 |
| 14 | WAYPOINT | 44 | 8 |
| 15 | LATLONALT | 24 | 8 |
| 16 | XYZ | 24 | 8 |
| 17 | INT8 | 1 | 1 |

## 3. Key Packet Structures

### AddToDataDefinition (SendId 12)

Client→server. Registers a variable in a data definition group.

```
@0   dwSize      4B
@4   dwVersion   4B
@8   dwID        4B    0xF000000C
@12  dwSendID    4B    Packet sequence counter
@16  DefineID    4B    Data definition group ID
@20  DatumName   256B  Variable name (null-terminated ASCII)
@276 UnitsName   256B  Unit string (null-terminated ASCII)
@532 DatumType   4B    SIMCONNECT_DATATYPE enum
@536 fEpsilon    4B    Change threshold (float)
@540 DatumID     4B    User datum ID (0xFFFFFFFF = unused)
```

**Proxy rewrite:** Overwrites DatumName (@20) and UnitsName (@276) in-place to redirect standard sim variables to L-Vars.

### RequestDataOnSimObject (SendId 14)

Client→server. Subscribes to periodic data delivery.

```
@0   dwSize       4B
@4   dwVersion    4B
@8   dwID         4B    0xF000000E
@12  dwSendID     4B    Packet sequence counter
@16  RequestID    4B    Client-assigned request ID
@20  DefineID     4B    Which definition to request
@24  ObjectID     4B    Target object (0 = user aircraft)
@28  Period       4B    SIMCONNECT_PERIOD enum
@32  Flags        4B    DATA_REQUEST_FLAG
@36  origin       4B    Periods to skip before start
@40  interval     4B    Periods between sends
@44  limit        4B    Max sends (0 = unlimited)
```

### RECV_SIMOBJECT_DATA (RecvId 8)

Server→client. Contains requested simulation data.

```
@0   dwSize        4B
@4   dwVersion     4B
@8   dwID=8        4B
@12  dwRequestID   4B    Matches RequestDataOnSimObject.RequestID
@16  dwObjectID    4B    Sim object ID
@20  dwDefineID    4B    Data definition ID
@24  dwFlags       4B    DATA_REQUEST_FLAG (tagged bit = 0x02)
@28  dwEntryNumber 4B    Entry N of M (1-based)
@32  dwOutOf       4B    Total entries
@36  dwDefineCount 4B    Number of data items
@40  [data...]     var   Payload — contiguous or tagged
```

**Proxy transform:** Payload at @40 is normalized — 8-byte L-Var FLOAT64 values cast back to 4-byte types the client expects, with buffer shift.

## 4. L-Var Rules

- **Format:** `"L:VarName"` (e.g., `"L:Eng1_RPM"`)
- **Data type:** Always `FLOAT64` (enum 4, 8 bytes) regardless of what the client requested
- **Units:** Must be `"Number"` (case-sensitive in the proxy's rewrite)
- **Scope:** Global — accessible from any aircraft gauge/module
- **Proxy impact:** When a standard variable is redirected to an L-Var, MSFS returns 8-byte FLOAT64 even if the client's definition declared INT32 or FLOAT32. The TransformationEngine must normalize the response.

## 5. Alignment Rule

Within a data definition's payload, 8-byte types (INT64, FLOAT64, INITPOSITION, WAYPOINT, LATLONALT, XYZ) require **8-byte aligned offsets**.

```
if (offset % 8 != 0) {
    padding = 8 - (offset % 8);
    offset += padding;
}
```

The proxy's `StateTracker` computes offsets when recording variable definitions to correctly locate each variable in the response payload.

## 6. String Encoding

- All string fields are **fixed-width**, **null-terminated ASCII**
- Remaining bytes after the null terminator are **zero-filled**
- DatumName (@20) and UnitsName (@276) slots in AddToDataDefinition are each 256 bytes
- szApplicationName in RECV_OPEN is 256 bytes
- When overwriting strings (proxy rewrite), clear the entire slot first, then write the new string

## 7. Detailed References

- **[references/binary-protocol.md](references/binary-protocol.md)** — Full byte-level layouts for all packet types relevant to the proxy (RECV_EXCEPTION, RECV_OPEN, RECV_EVENT, RECV_SYSTEM_STATE, complex data structures like INITPOSITION, LATLONALT, XYZ, WAYPOINT)
- **[references/enums.md](references/enums.md)** — Complete enum tables with numeric values for RECV_ID (40 values), DATATYPE (18 values), PERIOD, DATA_REQUEST_FLAG, SIMOBJECT_TYPE, EXCEPTION (45 error codes), and protocol constants
- **[references/configuration.md](references/configuration.md)** — SimConnect.cfg file format, protocol options, connection parameters, ConfigIndex mapping

## 8. Data Sources

All content verified against:
- `SimConnect SDK/include/SimConnect.h` — Authoritative enum values and struct definitions
- `MSFS 2024 SDK/Documentation/` — Official parameter docs and constraints
- `ComancheProxy/` source — Confirmed byte offsets from working proxy code

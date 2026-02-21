# SimConnect Enums Reference

All values from `SimConnect SDK/include/SimConnect.h` (MSFS 2024). Little-endian DWORD (uint32) unless noted.

## SIMCONNECT_RECV_ID

Server-to-client packet type in the 12-byte header `dwID` field.

| Value | Name | Notes |
|-------|------|-------|
| 0 | NULL | |
| 1 | EXCEPTION | Error response; see SIMCONNECT_EXCEPTION |
| 2 | OPEN | Connection established; contains version info |
| 3 | QUIT | Server shutting down |
| 4 | EVENT | Client event notification |
| 5 | EVENT_OBJECT_ADDREMOVE | Object created/removed |
| 6 | EVENT_FILENAME | Filename-based event (aircraft loaded, etc.) |
| 7 | EVENT_FRAME | Per-frame event with frame rate + sim speed |
| 8 | SIMOBJECT_DATA | **Proxy-critical.** Data response for RequestDataOnSimObject |
| 9 | SIMOBJECT_DATA_BYTYPE | Data response for RequestDataOnSimObjectType |
| 10 | WEATHER_OBSERVATION | |
| 11 | CLOUD_STATE | |
| 12 | ASSIGNED_OBJECT_ID | |
| 13 | RESERVED_KEY | |
| 14 | CUSTOM_ACTION | |
| 15 | SYSTEM_STATE | |
| 16 | CLIENT_DATA | ClientData response (same struct as SIMOBJECT_DATA) |
| 17 | EVENT_WEATHER_MODE | |
| 18 | AIRPORT_LIST | |
| 19 | VOR_LIST | |
| 20 | NDB_LIST | |
| 21 | WAYPOINT_LIST | |
| 22 | EVENT_MULTIPLAYER_SERVER_STARTED | |
| 23 | EVENT_MULTIPLAYER_CLIENT_STARTED | |
| 24 | EVENT_MULTIPLAYER_SESSION_ENDED | |
| 25 | EVENT_RACE_END | |
| 26 | EVENT_RACE_LAP | |
| 27 | EVENT_EX1 | Extended event with 5 DWORD data fields |
| 28 | FACILITY_DATA | |
| 29 | FACILITY_DATA_END | |
| 30 | FACILITY_MINIMAL_LIST | |
| 31 | JETWAY_DATA | |
| 32 | CONTROLLERS_LIST | |
| 33 | ACTION_CALLBACK | |
| 34 | ENUMERATE_INPUT_EVENTS | |
| 35 | GET_INPUT_EVENT | |
| 36 | SUBSCRIBE_INPUT_EVENT | |
| 37 | ENUMERATE_INPUT_EVENT_PARAMS | |
| 38 | ENUMERATE_SIMOBJECT_AND_LIVERY_LIST | |
| 39 | FLOW_EVENT | Flight flow state changes |

## SIMCONNECT_DATATYPE

Data type enum for `AddToDataDefinition` DatumType parameter.

| Value | Name | Size (bytes) | Notes |
|-------|------|-------------|-------|
| 0 | INVALID | 0 | |
| 1 | INT32 | 4 | 32-bit signed integer |
| 2 | INT64 | 8 | 64-bit signed integer |
| 3 | FLOAT32 | 4 | 32-bit float |
| 4 | FLOAT64 | 8 | 64-bit double. **L-Vars always use this.** |
| 5 | STRING8 | 8 | Fixed-width null-terminated ASCII |
| 6 | STRING32 | 32 | |
| 7 | STRING64 | 64 | |
| 8 | STRING128 | 128 | |
| 9 | STRING256 | 256 | |
| 10 | STRING260 | 260 | MAX_PATH equivalent |
| 11 | STRINGV | variable | Variable-length; prefixed by length |
| 12 | INITPOSITION | 56 | SIMCONNECT_DATA_INITPOSITION struct |
| 13 | MARKERSTATE | 68 | char[64] + DWORD |
| 14 | WAYPOINT | 44 | SIMCONNECT_DATA_WAYPOINT struct |
| 15 | LATLONALT | 24 | 3x double |
| 16 | XYZ | 24 | 3x double |
| 17 | INT8 | 1 | 8-bit signed integer (char) |

## SIMCONNECT_PERIOD

Request frequency for `RequestDataOnSimObject`.

| Value | Name | Description |
|-------|------|-------------|
| 0 | NEVER | Stop sending data |
| 1 | ONCE | Send once then stop |
| 2 | VISUAL_FRAME | Every visual frame |
| 3 | SIM_FRAME | Every simulation frame |
| 4 | SECOND | Every second |

## SIMCONNECT_DATA_REQUEST_FLAG

Flags for `RequestDataOnSimObject` and `RECV_SIMOBJECT_DATA.dwFlags`.

| Value | Name | Description |
|-------|------|-------------|
| 0x00 | DEFAULT | Send every period |
| 0x01 | CHANGED | Only send when value(s) change |
| 0x02 | TAGGED | Data in tagged format (DWORD id + value pairs) |

## SIMCONNECT_SIMOBJECT_TYPE

Object type filter for `RequestDataOnSimObjectType`.

| Value | Name |
|-------|------|
| 0 | USER (also USER_AIRCRAFT) |
| 1 | ALL |
| 2 | AIRCRAFT |
| 3 | HELICOPTER |
| 4 | BOAT |
| 5 | GROUND |
| 6 | HOT_AIR_BALLOON |
| 7 | ANIMAL |
| 8 | USER_AVATAR |
| 9 | USER_CURRENT |

## SIMCONNECT_EXCEPTION

Exception codes in `RECV_EXCEPTION.dwException`.

| Value | Name |
|-------|------|
| 0 | NONE |
| 1 | ERROR |
| 2 | SIZE_MISMATCH |
| 3 | UNRECOGNIZED_ID |
| 4 | UNOPENED |
| 5 | VERSION_MISMATCH |
| 6 | TOO_MANY_GROUPS |
| 7 | NAME_UNRECOGNIZED |
| 8 | TOO_MANY_EVENT_NAMES |
| 9 | EVENT_ID_DUPLICATE |
| 10 | TOO_MANY_MAPS |
| 11 | TOO_MANY_OBJECTS |
| 12 | TOO_MANY_REQUESTS |
| 13 | WEATHER_INVALID_PORT |
| 14 | WEATHER_INVALID_METAR |
| 15 | WEATHER_UNABLE_TO_GET_OBSERVATION |
| 16 | WEATHER_UNABLE_TO_CREATE_STATION |
| 17 | WEATHER_UNABLE_TO_REMOVE_STATION |
| 18 | INVALID_DATA_TYPE |
| 19 | INVALID_DATA_SIZE |
| 20 | DATA_ERROR |
| 21 | INVALID_ARRAY |
| 22 | CREATE_OBJECT_FAILED |
| 23 | LOAD_FLIGHTPLAN_FAILED |
| 24 | OPERATION_INVALID_FOR_OBJECT_TYPE |
| 25 | ILLEGAL_OPERATION |
| 26 | ALREADY_SUBSCRIBED |
| 27 | INVALID_ENUM |
| 28 | DEFINITION_ERROR |
| 29 | DUPLICATE_ID |
| 30 | DATUM_ID |
| 31 | OUT_OF_BOUNDS |
| 32 | ALREADY_CREATED |
| 33 | OBJECT_OUTSIDE_REALITY_BUBBLE |
| 34 | OBJECT_CONTAINER |
| 35 | OBJECT_AI |
| 36 | OBJECT_ATC |
| 37 | OBJECT_SCHEDULE |
| 38 | JETWAY_DATA |
| 39 | ACTION_NOT_FOUND |
| 40 | NOT_AN_ACTION |
| 41 | INCORRECT_ACTION_PARAMS |
| 42 | GET_INPUT_EVENT_FAILED |
| 43 | SET_INPUT_EVENT_FAILED |
| 44 | INTERNAL |

## SIMCONNECT_CLIENT_DATA_PERIOD

Request frequency for `RequestClientData`.

| Value | Name |
|-------|------|
| 0 | NEVER |
| 1 | ONCE |
| 2 | VISUAL_FRAME |
| 3 | ON_SET |
| 4 | SECOND |

## Constants

| Name | Value | Notes |
|------|-------|-------|
| OBJECT_ID_USER | 0 | User aircraft object ID |
| OBJECT_ID_USER_AIRCRAFT | 0 | Same as OBJECT_ID_USER |
| UNUSED | 0xFFFFFFFF | Sentinel for unused parameter |
| CLIENTDATA_MAX_SIZE | 8192 | Max ClientData area size |
| OPEN_CONFIGINDEX_LOCAL | 0xFFFFFFFF (-1) | Force local pipe connection |

namespace ComancheProxy.Protocol;

/// <summary>
/// SimConnect Recv IDs (server → client). Values from SimConnect.h.
/// </summary>
public enum RecvId : uint
{
    Null = 0,
    Exception = 1,
    Open = 2,
    Quit = 3,
    Event = 4,
    EventObjectAddremove = 5,
    EventFilename = 6,
    EventFrame = 7,
    SimobjectData = 8,
    SimobjectDataBytype = 9,
    WeatherObservation = 10,
    CloudState = 11,
    AssignedObjectId = 12,
    ReservedKey = 13,
    CustomAction = 14,
    SystemState = 15,
    ClientData = 16,
}

/// <summary>
/// SimConnect Send IDs (client → server). Low byte confirmed by hex analysis.
/// Wire format adds a 0xF0000000 prefix; use SendIdWire() for packet construction.
/// </summary>
public enum SendId : uint
{
    Open = 1,
    Close = 2,
    SubscribeToSystemEvent = 4,
    UnsubscribeFromSystemEvent = 5,
    MapClientEventToSimEvent = 8,
    TransmitClientEvent = 9,
    SetSystemEventState = 10,
    AddToDataDefinition = 12, // CONFIRMED by hex analysis (0x0C)
    ClearDataDefinition = 13,
    RequestDataOnSimObject = 14, // CONFIRMED by hex analysis (0x0E)
    SetDataOnSimObject = 16,
}

/// <summary>
/// Extension to produce wire-format SendId values (with 0xF0000000 prefix).
/// </summary>
public static class SendIdExtensions
{
    /// <summary>
    /// Returns the wire-format value for writing into outbound packets.
    /// </summary>
    public static uint Wire(this SendId id) => 0xF0000000 | (uint)id;
}

/// <summary>
/// SimConnect data types. Values from SimConnect.h SIMCONNECT_DATATYPE enum.
/// </summary>
public enum SimConnectDataType : uint
{
    Invalid = 0,
    Int32 = 1,
    Int64 = 2,
    Float32 = 3,
    Float64 = 4,
    String8 = 5,
    String32 = 6,
    String64 = 7,
    String128 = 8,
    String256 = 9,
    String260 = 10,
    StringV = 11,
    InitPosition = 12,
    MarkerState = 13,
    Waypoint = 14,
    LatLonAlt = 15,
    Xyz = 16,
    Int8 = 17,
}

/// <summary>
/// Data type sizes in bytes for alignment and offset calculations.
/// </summary>
public static class DataTypeSize
{
    /// <summary>
    /// Returns the byte size of the given SimConnect data type.
    /// </summary>
    public static uint GetSize(SimConnectDataType type) => type switch
    {
        SimConnectDataType.Invalid => 0,
        SimConnectDataType.Int32 => 4,
        SimConnectDataType.Int64 => 8,
        SimConnectDataType.Float32 => 4,
        SimConnectDataType.Float64 => 8,
        SimConnectDataType.String8 => 8,
        SimConnectDataType.String32 => 32,
        SimConnectDataType.String64 => 64,
        SimConnectDataType.String128 => 128,
        SimConnectDataType.String256 => 256,
        SimConnectDataType.String260 => 260,
        SimConnectDataType.InitPosition => 56,
        SimConnectDataType.MarkerState => 68,
        SimConnectDataType.Waypoint => 44,
        SimConnectDataType.LatLonAlt => 24,
        SimConnectDataType.Xyz => 24,
        SimConnectDataType.Int8 => 1,
        _ => 8
    };
}

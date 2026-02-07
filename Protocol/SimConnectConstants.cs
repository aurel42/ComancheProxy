namespace ComancheProxy.Protocol;

/// <summary>
/// Common SimConnect Recv IDs.
/// </summary>
public enum RecvId : uint
{
    Null = 0,
    Exception = 1,
    Open = 2,
    Quit = 3,
    Event = 4,
    EventFilename = 5,
    EventObjectAddremove = 6,
    SimobjectData = 8,
    SimobjectDataBytype = 9,
    SystemState = 14,
    ClientData = 15,
}

/// <summary>
/// Common SimConnect Send IDs (from client to sim).
/// </summary>
public enum SendId : uint
{
    Open = 1,
    Close = 2,
    SubscribeToSystemEvent = 4,
    UnsubscribeFromSystemEvent = 5,
    MapClientEventToSimEvent = 8,
    TransmitClientEvent = 9,
    AddToDataDefinition = 11,
    RequestDataOnSimObject = 12,
}

public enum SimConnectDataType : uint
{
    Int32 = 1,
    Int64 = 2,
    Float32 = 3,
    Float64 = 4,
    String8 = 5,
    String32 = 6,
    String64 = 7,
    String128 = 8,
    String256 = 9,
    StringV = 10,
}

/// <summary>
/// Data type sizes for alignment calculations.
/// </summary>
public static class DataTypeSize
{
    public static uint GetSize(SimConnectDataType type) => type switch
    {
        SimConnectDataType.Int32 => 4,
        SimConnectDataType.Int64 => 8,
        SimConnectDataType.Float32 => 4,
        SimConnectDataType.Float64 => 8,
        SimConnectDataType.String8 => 8,
        SimConnectDataType.String32 => 32,
        SimConnectDataType.String64 => 64,
        SimConnectDataType.String128 => 128,
        SimConnectDataType.String256 => 256,
        _ => 8
    };
}

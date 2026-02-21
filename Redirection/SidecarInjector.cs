using System.Buffers.Binary;
using System.Text;
using ComancheProxy.Protocol;

namespace ComancheProxy.Redirection;

/// <summary>
/// Injects sidecar L-Var subscriptions into the SimConnect TCP connection and
/// provides override values for client-facing variables. The A2A Comanche exposes
/// AP state through five status-light L-Vars rather than AUTOPILOT MASTER, and
/// engine data through dedicated L-Vars for RPM and thrust.
/// </summary>
public sealed class SidecarInjector
{
    /// <summary>
    /// High DefineID to avoid collision with client-assigned IDs.
    /// </summary>
    public const uint SidecarDefineId = 0xFFFE0000;

    /// <summary>
    /// RequestID for the sidecar data subscription.
    /// </summary>
    public const uint SidecarRequestId = 0xFFFE0000;

    private static readonly string[] SidecarLVars =
    [
        "L:ApStLight",
        "L:ApHdLight",
        "L:ApTrkHiLight",
        "L:ApTrkLoLight",
        "L:ApAltLight",
        "L:Eng1_RPM",
        "L:Eng1_Thrust"
    ];

    private readonly double[] _values = new double[7];

    /// <summary>
    /// Thread-safe aggregate autopilot state. True when any AP light L-Var is active.
    /// </summary>
    public volatile bool IsAutopilotActive;

    /// <summary>
    /// Attempts to get an override value for a client-facing SimConnect variable.
    /// </summary>
    /// <param name="simVarName">The original SimConnect variable name (trimmed).</param>
    /// <param name="value">The override value if available.</param>
    /// <returns>True if an override exists for this variable.</returns>
    public bool TryGetOverrideValue(string simVarName, out double value)
    {
        if (simVarName.Equals("AUTOPILOT MASTER", StringComparison.OrdinalIgnoreCase))
        {
            value = IsAutopilotActive ? 1.0 : 0.0;
            return true;
        }

        // RPM: sim value (percent of max) appears correct for CLS2Sim — no override needed
        // if (simVarName.Equals("GENERAL ENG PCT MAX RPM:1", StringComparison.OrdinalIgnoreCase))
        // {
        //     value = _values[5];
        //     return true;
        // }

        if (simVarName.Equals("PROP THRUST:1", StringComparison.OrdinalIgnoreCase))
        {
            value = _values[6];
            return true;
        }

        value = 0.0;
        return false;
    }

    /// <summary>
    /// Builds the injection payload: 7 AddToDataDefinition packets + 1 RequestDataOnSimObject.
    /// </summary>
    /// <param name="dwVersion">The protocol version captured from the client's first packet.</param>
    /// <returns>Concatenated binary packets ready to write to the MSFS stream.</returns>
    public byte[] BuildInjectionPackets(uint dwVersion)
    {
        const int addPacketSize = 544;
        const int reqPacketSize = 48;
        int totalSize = (SidecarLVars.Length * addPacketSize) + reqPacketSize;

        byte[] buffer = new byte[totalSize];
        int offset = 0;

        foreach (string varName in SidecarLVars)
        {
            WriteAddToDataDefinition(buffer.AsSpan(offset, addPacketSize), dwVersion, varName);
            offset += addPacketSize;
        }

        WriteRequestDataOnSimObject(buffer.AsSpan(offset, reqPacketSize), dwVersion);

        return buffer;
    }

    /// <summary>
    /// Builds a ClearDataDefinition packet to unsubscribe the sidecar definition.
    /// </summary>
    /// <param name="dwVersion">The protocol version captured from the client's first packet.</param>
    /// <returns>Binary packet ready to write to the MSFS stream.</returns>
    public byte[] BuildCleanupPacket(uint dwVersion)
    {
        byte[] buffer = new byte[20];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span, 20);                                     // dwSize
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), dwVersion);                     // dwVersion
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), Protocol.SendId.ClearDataDefinition.Wire());
        // Offset 12: dwSendID (sequence) — left as 0
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16), SidecarDefineId);              // DefineID

        return buffer;
    }

    /// <summary>
    /// Returns true if the given requestId belongs to the sidecar subscription.
    /// </summary>
    public static bool IsSidecarResponse(uint requestId) => requestId == SidecarRequestId;

    /// <summary>
    /// Processes a RECV_SIMOBJECT_DATA payload from our sidecar subscription.
    /// Reads 7 FLOAT64 values: 5 AP lights + Eng1_RPM + Eng1_Thrust.
    /// Derives aggregate AP state from the first 5.
    /// </summary>
    /// <param name="dataBlock">The data payload starting after the 40-byte RECV_SIMOBJECT_DATA header.</param>
    /// <returns>True if autopilot state changed.</returns>
    public bool ProcessResponse(ReadOnlySpan<byte> dataBlock)
    {
        bool anyApActive = false;

        for (int i = 0; i < SidecarLVars.Length; i++)
        {
            int varOffset = i * 8;
            if (dataBlock.Length >= varOffset + 8)
            {
                double val = BinaryPrimitives.ReadDoubleLittleEndian(dataBlock.Slice(varOffset, 8));
                _values[i] = val;

                // First 5 are AP lights — OR them for aggregate state
                if (i < 5 && val > 0.5)
                {
                    anyApActive = true;
                }
            }
        }

        bool previousState = IsAutopilotActive;
        IsAutopilotActive = anyApActive;
        return previousState != anyApActive;
    }

    /// <summary>
    /// Writes a single AddToDataDefinition packet for an L-Var.
    /// </summary>
    private static void WriteAddToDataDefinition(Span<byte> packet, uint dwVersion, string varName)
    {
        packet.Clear();

        BinaryPrimitives.WriteUInt32LittleEndian(packet, 544);                                           // dwSize
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(4), dwVersion);                            // dwVersion
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(8), Protocol.SendId.AddToDataDefinition.Wire());
        // Offset 12: dwSendID (sequence) — left as 0
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(16), SidecarDefineId);                     // DefineID

        Encoding.ASCII.GetBytes(varName, packet.Slice(20, 256));
        Encoding.ASCII.GetBytes("Number", packet.Slice(276, 256));

        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(532), (uint)Protocol.SimConnectDataType.Float64);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(540), 0xFFFFFFFF);                         // DatumID
    }

    /// <summary>
    /// Writes a RequestDataOnSimObject packet for the sidecar definition.
    /// </summary>
    private static void WriteRequestDataOnSimObject(Span<byte> packet, uint dwVersion)
    {
        packet.Clear();

        BinaryPrimitives.WriteUInt32LittleEndian(packet, 48);                                                  // dwSize
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(4), dwVersion);                                  // dwVersion
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(8), Protocol.SendId.RequestDataOnSimObject.Wire());
        // Offset 12: dwSendID (sequence) — left as 0
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(16), SidecarRequestId);                          // RequestID
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(20), SidecarDefineId);                           // DefineID
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(24), 0);                                         // ObjectID = user aircraft
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(28), 2);                                         // Period = VISUAL_FRAME
    }
}

using System.Buffers.Binary;
using System.Text;
using ComancheProxy.Protocol;
using ComancheProxy.Models;

namespace ComancheProxy.Redirection;

/// <summary>
/// Injects sidecar L-Var subscriptions into the SimConnect TCP connection and
/// provides override values for client-facing variables based on aircraft features.
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

    private readonly List<string> _activeLVars = new();
    private readonly Dictionary<string, double> _lVarValues = new();

    private AircraftProfile? _activeProfile;

    /// <summary>
    /// Thread-safe aggregate autopilot state.
    /// </summary>
    public volatile bool IsAutopilotActive;

    /// <summary>
    /// Resets all sidecar state between bridge sessions.
    /// </summary>
    public void Reset()
    {
        IsAutopilotActive = false;
        _activeLVars.Clear();
        _lVarValues.Clear();
        _activeProfile = null;
    }

    /// <summary>
    /// Attempts to get an override value for a client-facing SimConnect variable.
    /// </summary>
    public bool TryGetOverrideValue(string simVarName, out double value)
    {
        value = 0.0;
        if (_activeProfile == null) return false;

        var features = _activeProfile.Features;

        if (features.Autopilot.Enabled && simVarName.Equals("AUTOPILOT MASTER", StringComparison.OrdinalIgnoreCase))
        {
            value = IsAutopilotActive ? 1.0 : 0.0;
            return true;
        }

        if (features.PropellerThrust.Enabled && simVarName.Equals("PROP THRUST:1", StringComparison.OrdinalIgnoreCase))
        {
            if (_lVarValues.TryGetValue(features.PropellerThrust.SourceLVar, out double thrust))
            {
                value = thrust;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the injection payload for the given profile.
    /// Returns null if no L-Vars are required.
    /// </summary>
    public byte[]? BuildInjectionPackets(uint dwVersion, AircraftProfile profile)
    {
        _activeProfile = profile;
        _activeLVars.Clear();

        if (profile.Features.Autopilot.Enabled)
        {
            foreach (var lvar in profile.Features.Autopilot.SourceLVars)
            {
                if (!_activeLVars.Contains(lvar)) _activeLVars.Add(lvar);
            }
        }

        if (profile.Features.PropellerThrust.Enabled)
        {
            string lvar = profile.Features.PropellerThrust.SourceLVar;
            if (!string.IsNullOrEmpty(lvar) && !_activeLVars.Contains(lvar))
            {
                _activeLVars.Add(lvar);
            }
        }

        if (_activeLVars.Count == 0) return null;

        const int addPacketSize = 544;
        const int reqPacketSize = 48;
        int totalSize = (_activeLVars.Count * addPacketSize) + reqPacketSize;

        byte[] buffer = new byte[totalSize];
        int offset = 0;

        foreach (string varName in _activeLVars)
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
    public byte[] BuildCleanupPacket(uint dwVersion)
    {
        byte[] buffer = new byte[20];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span, 20);                                     // dwSize
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), dwVersion);                     // dwVersion
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), Protocol.SendId.ClearDataDefinition.Wire());
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16), SidecarDefineId);              // DefineID

        return buffer;
    }

    /// <summary>
    /// Returns true if the given requestId belongs to the sidecar subscription.
    /// </summary>
    public static bool IsSidecarResponse(uint requestId) => requestId == SidecarRequestId;

    /// <summary>
    /// Processes a RECV_SIMOBJECT_DATA payload from our sidecar subscription.
    /// </summary>
    public bool ProcessResponse(ReadOnlySpan<byte> dataBlock)
    {
        if (_activeProfile == null) return false;

        for (int i = 0; i < _activeLVars.Count; i++)
        {
            int varOffset = i * 8;
            if (dataBlock.Length >= varOffset + 8)
            {
                double val = BinaryPrimitives.ReadDoubleLittleEndian(dataBlock.Slice(varOffset, 8));
                _lVarValues[_activeLVars[i]] = val;
            }
        }

        bool anyApActive = false;
        if (_activeProfile.Features.Autopilot.Enabled)
        {
            foreach (var lvar in _activeProfile.Features.Autopilot.SourceLVars)
            {
                if (_lVarValues.TryGetValue(lvar, out double val) && val > 0.5)
                {
                    anyApActive = true;
                    break;
                }
            }
        }

        bool previousState = IsAutopilotActive;
        IsAutopilotActive = anyApActive;
        return previousState != anyApActive;
    }

    private static void WriteAddToDataDefinition(Span<byte> packet, uint dwVersion, string varName)
    {
        packet.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(packet, 544);                                           // dwSize
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(4), dwVersion);                            // dwVersion
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(8), Protocol.SendId.AddToDataDefinition.Wire());
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(16), SidecarDefineId);                     // DefineID

        Encoding.ASCII.GetBytes(varName, packet.Slice(20, 256));
        Encoding.ASCII.GetBytes("Number", packet.Slice(276, 256));

        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(532), (uint)Protocol.SimConnectDataType.Float64);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(540), 0xFFFFFFFF);                         // DatumID
    }

    private static void WriteRequestDataOnSimObject(Span<byte> packet, uint dwVersion)
    {
        packet.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(packet, 48);                                                  // dwSize
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(4), dwVersion);                                  // dwVersion
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(8), Protocol.SendId.RequestDataOnSimObject.Wire());
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(16), SidecarRequestId);                          // RequestID
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(20), SidecarDefineId);                           // DefineID
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(24), 0);                                         // ObjectID = user aircraft
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(28), 2);                                         // Period = VISUAL_FRAME
    }
}

using System.Collections.Concurrent;
using ComancheProxy.Models;

namespace ComancheProxy;

/// <summary>
/// Tracks the state of SimConnect definitions and the currently active aircraft.
/// </summary>
public sealed class StateTracker(ProxyLogger logger)
{
    private readonly ConcurrentDictionary<uint, DataDefinition> _definitions = new();
    private readonly ConcurrentDictionary<uint, uint> _requestToDefinition = new();
    
    public bool IsComancheMode { get; private set; }

    /// <summary>
    /// Records an AddToDataDefinition call.
    /// </summary>
    public void AddVariableToDefinition(uint defineId, string name, string unit, uint dataType)
    {
        var definition = _definitions.GetOrAdd(defineId, id => new DataDefinition { DefineId = id });
        
        var type = (Protocol.SimConnectDataType)dataType;
        uint size = Protocol.DataTypeSize.GetSize(type);

        // SimConnect alignment rule: 8-byte types (Float64, Int64) must be 8-byte aligned.
        if (size == 8)
        {
            uint remainder = definition.TotalSize % 8;
            if (remainder > 0)
            {
                definition.TotalSize += (8 - remainder); // Add padding
            }
        }

        uint offset = definition.TotalSize;
        definition.Variables.Enqueue(new VariableMetadata(name, unit, dataType, offset, size));
        definition.TotalSize += size;

        logger.LogPacketTrace(defineId, definition.TotalSize);
    }

    /// <summary>
    /// Maps a RequestId to a DefineId.
    /// </summary>
    public void MapRequest(uint requestId, uint defineId)
    {
        _requestToDefinition[requestId] = defineId;
    }

    /// <summary>
    /// Evaluates if the current aircraft is the Comanche.
    /// </summary>
    public void UpdateAircraftTitle(ReadOnlySpan<byte> titleData)
    {
        // Simple string check on the binary data
        string title = System.Text.Encoding.ASCII.GetString(titleData).TrimEnd('\0');
        bool wasComanche = IsComancheMode;
        IsComancheMode = title.Contains("A2A PA24-250 Comanche", StringComparison.OrdinalIgnoreCase);

        if (wasComanche != IsComancheMode)
        {
            logger.LogStarted(IsComancheMode ? "COMANCHE MODE ACTIVATED" : "COMANCHE MODE DEACTIVATED");
        }
    }

    public DataDefinition? GetDefinitionForRequest(uint requestId)
    {
        return _requestToDefinition.TryGetValue(requestId, out uint defineId) 
            ? _definitions.GetValueOrDefault(defineId) 
            : null;
    }
}

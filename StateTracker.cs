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
    private readonly ConcurrentDictionary<uint, string> _eventMappings = new();

    private volatile AircraftProfile? _activeProfile;

    /// <summary>
    /// The currently active aircraft profile, if any.
    /// </summary>
    public AircraftProfile? ActiveProfile
    {
        get => _activeProfile;
        set => _activeProfile = value;
    }

    /// <summary>
    /// Clears all tracked definitions, request mappings, and event mappings.
    /// Called between bridge sessions to prevent stale state accumulation.
    /// </summary>
    public void Reset()
    {
        _definitions.Clear();
        _requestToDefinition.Clear();
        _eventMappings.Clear();
        ActiveProfile = null;
    }

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
        definition.Variables.Add(new VariableMetadata(name.Trim(), unit.Trim(), dataType, offset, size));
        definition.TotalSize += size;

        logger.LogDefinitionTrace(defineId, definition.TotalSize);
    }

    /// <summary>
    /// Maps a RequestId to a DefineId.
    /// </summary>
    public void MapRequest(uint requestId, uint defineId)
    {
        _requestToDefinition[requestId] = defineId;
    }

    public DataDefinition? GetDefinitionForRequest(uint requestId)
    {
        return _requestToDefinition.TryGetValue(requestId, out uint defineId)
            ? _definitions.GetValueOrDefault(defineId)
            : null;
    }

    /// <summary>
    /// Records a MapClientEventToSimEvent mapping.
    /// </summary>
    public void MapEvent(uint eventId, string eventName)
    {
        _eventMappings[eventId] = eventName;
    }

    /// <summary>
    /// Resolves a client event ID to its mapped sim event name.
    /// </summary>
    public bool TryGetEventName(uint eventId, out string? name)
    {
        return _eventMappings.TryGetValue(eventId, out name);
    }

    /// <summary>
    /// Returns a summary of variable names for a given DefineID, for diagnostic logging.
    /// </summary>
    public string GetDefinitionSummary(uint defineId)
    {
        if (_definitions.TryGetValue(defineId, out var def))
        {
            return string.Join(", ", def.Variables.Select(v => v.Name));
        }
        return $"(unknown DefineID={defineId})";
    }
}

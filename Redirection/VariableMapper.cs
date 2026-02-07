using System.Collections.Concurrent;

namespace ComancheProxy.Redirection;

/// <summary>
/// Defines the mapping between standard SimConnect variables and high-fidelity L-Vars.
/// </summary>
public sealed class VariableMapper
{
    private readonly ConcurrentDictionary<string, string> _mappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GENERAL ENG RPM:1"] = "L:Eng1_RPM",
        ["AIRSPEED INDICATED"] = "L:AirspeedIndicated",
        ["AUTOPILOT MASTER"] = "L:ApMaster"
    };

    public bool TryGetRedirection(string simVar, out string? lVar)
    {
        return _mappings.TryGetValue(simVar, out lVar);
    }
}

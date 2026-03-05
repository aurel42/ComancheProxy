namespace ComancheProxy.Models;

/// <summary>
/// Identifies the type of SimConnect client connected to a proxy port.
/// </summary>
public enum ClientProfile
{
    /// <summary>CLS2Sim force-feedback client — receives sidecar injection and data overrides.</summary>
    CLS2Sim,
    /// <summary>FSEconomy client — receives aircraft title spoofing only.</summary>
    FSEconomy
}

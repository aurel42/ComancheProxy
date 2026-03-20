using System.Text.Json.Serialization;

namespace ComancheProxy.Models;

public sealed class ProxyConfig
{
    public List<AircraftProfile> AircraftProfiles { get; set; } = new();
    public int FsePort { get; set; }
    public int CLS2SimPort { get; set; } = 5001;
    public List<TitleMapping> TitleMappings { get; set; } = new();
}

/// <summary>
/// Maps a partial aircraft title match to a replacement string for FSEconomy spoofing.
/// </summary>
public sealed class TitleMapping
{
    public string Match { get; set; } = string.Empty;
    public string Replace { get; set; } = string.Empty;
}

public sealed class AircraftProfile
{
    public string Name { get; set; } = string.Empty;
    public string MatchPattern { get; set; } = string.Empty;

    private byte[]? _matchPatternBytes;
    [JsonIgnore]
    public byte[] MatchPatternBytes => _matchPatternBytes ??= System.Text.Encoding.ASCII.GetBytes(MatchPattern);

    public ProfileFeatures Features { get; set; } = new();
}

public sealed class ProfileFeatures
{
    public TrimFeatureConfig Trim { get; set; } = new();
    public AutopilotFeatureConfig Autopilot { get; set; } = new();
    public PropellerThrustConfig PropellerThrust { get; set; } = new();
}

public sealed class TrimFeatureConfig
{
    public bool Enabled { get; set; }
    public float CenterValue { get; set; }
}

public sealed class AutopilotFeatureConfig
{
    public bool Enabled { get; set; }
    public List<string> SourceLVars { get; set; } = new();
}

public sealed class PropellerThrustConfig
{
    public bool Enabled { get; set; }
    public string SourceLVar { get; set; } = string.Empty;
}

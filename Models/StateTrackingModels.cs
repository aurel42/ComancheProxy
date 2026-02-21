namespace ComancheProxy.Models;

/// <summary>
/// Metadata for a single variable within a data definition.
/// </summary>
public record VariableMetadata(
    string Name,
    string Unit,
    uint DataType,
    uint Offset,
    uint Size);

/// <summary>
/// Represents a grouped data definition requested by the client.
/// </summary>
public sealed class DataDefinition
{
    /// <summary>The SimConnect DefineID this definition corresponds to.</summary>
    public uint DefineId { get; init; }

    /// <summary>Ordered list of variables in this definition. Populated during setup, iterated on the hot path.</summary>
    public List<VariableMetadata> Variables { get; } = [];

    /// <summary>Total size including padding.</summary>
    public uint TotalSize { get; set; }
}

/// <summary>
/// Represents a data request association.
/// </summary>
public record RequestMetadata(uint DefineId, uint RequestId);

using System.Collections.Concurrent;

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
    public uint DefineId { get; init; }
    public ConcurrentQueue<VariableMetadata> Variables { get; } = new();
    
    // Total size including padding
    public uint TotalSize { get; set; }
}

/// <summary>
/// Represents a data request association.
/// </summary>
public record RequestMetadata(uint DefineId, uint RequestId);

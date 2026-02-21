using System.Buffers.Binary;
using ComancheProxy.Models;
using Microsoft.Extensions.Logging;

namespace ComancheProxy.Redirection;

/// <summary>
/// Observes and potentially normalizes SimConnect data streams.
/// 
/// DESIGN:
/// 1. Native Redirection swaps variable names in the request.
/// 2. MSFS returns L-Var data natively, but often forces 8-byte FLOAT64.
/// 3. TransformationEngine normalizes this data back to the client's expected 4-byte types
///    and shifts the buffer to maintain alignment.
/// </summary>
public sealed class TransformationEngine(ProxyLogger logger)
{
    private double? _lastApMasterValue;
    /// <summary>
    /// Inspects and potentially normalizes a raw SimConnect data block.
    /// If an L-Var was returned as 8 bytes but the client expects 4, it casts and shifts the buffer.
    /// </summary>
    /// <returns>The new effective length of the data block.</returns>
    public int NormalizePayload(Span<byte> dataBlock, DataDefinition definition)
    {
        int shift = 0;
        
        foreach (var variable in definition.Variables)
        {
            int currentOffset = (int)variable.Offset - shift;
            
            // 1. MONITOR BEFORE SHIFT: If it's the AP Master, check it now while we have the raw data.
            string varName = variable.OriginalName ?? variable.Name;
            if (varName.Trim().Equals("AUTOPILOT MASTER", StringComparison.OrdinalIgnoreCase))
            {
                // We read with the 'Size' the simulator actually sent (8 for L-Var, variable.Size otherwise)
                uint actualSize = variable.IsRedirected ? 8u : variable.Size;
                if (dataBlock.Length >= (uint)currentOffset + actualSize)
                {
                    double value = ReadValue(dataBlock.Slice(currentOffset, (int)actualSize), 
                                            variable.IsRedirected ? (uint)Protocol.SimConnectDataType.Float64 : variable.DataType);
                    
                    if (!_lastApMasterValue.HasValue || Math.Abs(_lastApMasterValue.Value - value) > 0.01)
                    {
                        _lastApMasterValue = value;
                        logger.LogApState(value > 0.5 ? "ON" : "OFF");
                        logger.LogInfo($"AP MONITOR: {varName} = {value} (Redirected={variable.IsRedirected})");
                    }
                }
            }

            // 2. NORMALIZE: If L-Var (8 bytes) took a 4-byte slot, shift it.
            if (variable.IsRedirected && variable.Size == 4)
            {
                if (dataBlock.Length >= currentOffset + 8)
                {
                    double value = BinaryPrimitives.ReadDoubleLittleEndian(dataBlock.Slice(currentOffset, 8));
                    
                    switch ((Protocol.SimConnectDataType)variable.DataType)
                    {
                        case Protocol.SimConnectDataType.Int32:
                            BinaryPrimitives.WriteInt32LittleEndian(dataBlock.Slice(currentOffset, 4), (int)Math.Round(value));
                            break;
                        case Protocol.SimConnectDataType.Float32:
                            BinaryPrimitives.WriteSingleLittleEndian(dataBlock.Slice(currentOffset, 4), (float)value);
                            break;
                    }
                    
                    int remainingBytes = dataBlock.Length - (currentOffset + 8);
                    if (remainingBytes > 0)
                    {
                        dataBlock.Slice(currentOffset + 8, remainingBytes).CopyTo(dataBlock.Slice(currentOffset + 4));
                    }
                    
                    shift += 4;
                    logger.LogInfo($"NORMALIZED: {variable.Name} (FLOAT64 -> {variable.DataType}) value={value}");
                }
            }
        }
        
        return dataBlock.Length - shift;
    }

    /// <summary>
    /// Inspects a raw SimConnect data block for important state transitions.
    /// OBSOLETE: Now handled during NormalizePayload to ensure offset accuracy.
    /// </summary>
    public void InspectPayload(ReadOnlySpan<byte> dataBlock, DataDefinition definition)
    {
        // No-op: logging is integrated into NormalizePayload for the Comanche definition.
    }

    private static double ReadValue(ReadOnlySpan<byte> data, uint dataType)
    {
        return (Protocol.SimConnectDataType)dataType switch
        {
            Protocol.SimConnectDataType.Float64 => BinaryPrimitives.ReadDoubleLittleEndian(data),
            Protocol.SimConnectDataType.Float32 => BinaryPrimitives.ReadSingleLittleEndian(data),
            Protocol.SimConnectDataType.Int32 => BinaryPrimitives.ReadInt32LittleEndian(data),
            Protocol.SimConnectDataType.Int64 => BinaryPrimitives.ReadInt64LittleEndian(data),
            _ => 0.0
        };
    }
}

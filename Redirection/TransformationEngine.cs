using System.Buffers.Binary;
using ComancheProxy.Models;

namespace ComancheProxy.Redirection;

/// <summary>
/// Overwrites SimConnect data values with sidecar-derived L-Var values when
/// the A2A Comanche is active. Iterates each variable in the definition and
/// applies overrides from the SidecarInjector.
/// </summary>
public sealed class TransformationEngine(SidecarInjector sidecarInjector)
{
    /// <summary>
    /// Applies sidecar overrides to a raw SimConnect data block.
    /// For each variable in the definition, checks if the SidecarInjector has an
    /// override value and writes it in the variable's native format.
    /// </summary>
    public void NormalizePayload(Span<byte> dataBlock, DataDefinition definition, bool isComancheMode)
    {
        if (!isComancheMode) return;

        foreach (var variable in definition.Variables)
        {
            int currentOffset = (int)variable.Offset;

            string varName = variable.Name.Trim();
            if (sidecarInjector.TryGetOverrideValue(varName, out double overrideValue))
            {
                if (dataBlock.Length >= currentOffset + (int)variable.Size)
                {
                    switch ((Protocol.SimConnectDataType)variable.DataType)
                    {
                        case Protocol.SimConnectDataType.Int32:
                            BinaryPrimitives.WriteInt32LittleEndian(dataBlock.Slice(currentOffset, 4), (int)Math.Round(overrideValue));
                            break;
                        case Protocol.SimConnectDataType.Float32:
                            BinaryPrimitives.WriteSingleLittleEndian(dataBlock.Slice(currentOffset, 4), (float)overrideValue);
                            break;
                        case Protocol.SimConnectDataType.Float64:
                            BinaryPrimitives.WriteDoubleLittleEndian(dataBlock.Slice(currentOffset, 8), overrideValue);
                            break;
                    }
                }
            }
        }
    }
}

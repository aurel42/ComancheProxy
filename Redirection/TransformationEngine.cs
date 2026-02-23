using System.Buffers.Binary;
using ComancheProxy.Models;

namespace ComancheProxy.Redirection;

/// <summary>
/// Overwrites SimConnect data values with sidecar-derived L-Var values when
/// the A2A Comanche is active. Iterates each variable in the definition and
/// applies overrides from the SidecarInjector.
/// </summary>
public sealed class TransformationEngine(SidecarInjector sidecarInjector, StateTracker stateTracker)
{
    /// <summary>
    /// Applies sidecar overrides and elevator trim recentering to a raw SimConnect data block.
    /// </summary>
    public void NormalizePayload(Span<byte> dataBlock, DataDefinition definition)
    {
        var profile = stateTracker.ActiveProfile;
        if (profile == null) return;

        foreach (var variable in definition.Variables)
        {
            int currentOffset = (int)variable.Offset;
            string varName = variable.Name;

            // Recenter elevator trim: symmetric linear scaling around configured center
            if (profile.Features.Trim.Enabled && varName == "ELEVATOR TRIM PCT"
                && dataBlock.Length >= currentOffset + (int)variable.Size)
            {
                float center = profile.Features.Trim.CenterValue;
                // Use the distance to the closer edge as the symmetric half-range.
                float halfRange = 1.0f - Math.Abs(center);
                if (halfRange > 0)
                {
                    float raw = BinaryPrimitives.ReadSingleLittleEndian(dataBlock.Slice(currentOffset, 4));
                    float recentered = Math.Clamp((raw - center) / halfRange, -1.0f, 1.0f);
                    BinaryPrimitives.WriteSingleLittleEndian(dataBlock.Slice(currentOffset, 4), recentered);
                }
            }

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

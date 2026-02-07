using System.Buffers.Binary;
using ComancheProxy.Models;
using Microsoft.Extensions.Logging;

namespace ComancheProxy.Redirection;

/// <summary>
/// Patches SimConnect binary payloads with high-fidelity data.
/// </summary>
public sealed class TransformationEngine(
    ILVarProvider lVarProvider,
    VariableMapper mapper,
    ProxyLogger logger)
{
    /// <summary>
    /// Patches a SimObjectData packet if Comanche mode is active and redirections are found.
    /// </summary>
    /// <param name="payload">The binary payload (after the 12-byte SimConnect header).</param>
    /// <param name="definition">The tracking definition for this data block.</param>
    public void PatchPayload(Span<byte> payload, DataDefinition definition)
    {
        // Skip the SimConnect metadata in RECV_SIMOBJECT_DATA (dwRequestID, dwObjectID, dwDefineID, etc.)
        // Data starts at offset 12 from the start of the payload (which is offset 24 from the start of the packet)
        Span<byte> dataBlock = payload.Slice(12);

        foreach (var variable in definition.Variables)
        {
            if (mapper.TryGetRedirection(variable.Name, out string? lVar) && lVar != null)
            {
                if (dataBlock.Length >= variable.Offset + variable.Size)
                {
                    double highFidelityValue = lVarProvider.GetValue(lVar);
                    
                    // Always log on patching in debug mode (performance checked by LoggerMessage)
                    logger.LogPacketTrace(definition.DefineId, (uint)highFidelityValue);

                    // Cast/Format based on original data type (assuming Float64 for MVP)
                    Span<byte> target = dataBlock.Slice((int)variable.Offset, (int)variable.Size);
                    BinaryPrimitives.WriteDoubleLittleEndian(target, highFidelityValue);
                }
            }
        }
    }
}

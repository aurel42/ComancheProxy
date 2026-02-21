using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Linq;
using System.Runtime.InteropServices;
using ComancheProxy.Tcp;
using ComancheProxy.Redirection;
using Microsoft.Extensions.Logging;

namespace ComancheProxy;


/// <summary>
/// Manages the bidirectional bridge between the client and the simulator.
/// </summary>
public sealed class ProxyBridge(
    TcpClient clientSocket, 
    TcpClient simSocket, 
    ProxyLogger logger,
    StateTracker stateTracker,
    TransformationEngine transformationEngine,
    VariableMapper mapper)
{
    private readonly CancellationTokenSource _cts = new();
    private long _clientPacketCounter = 0;

    public async Task RunAsync(CancellationToken externalCt)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt, _cts.Token);
        var ct = linkedCts.Token;

        string endpoints = $"{clientSocket.Client.RemoteEndPoint} <-> {simSocket.Client.RemoteEndPoint}";
        logger.LogBridgeStarted(endpoints);

        var upstreamStream = clientSocket.GetStream();
        var downstreamStream = simSocket.GetStream();

        var upstreamFramer = new SimConnectFramer(upstreamStream);
        var downstreamFramer = new SimConnectFramer(downstreamStream);

        var taskUp = PumpUpstreamToDownstreamAsync(upstreamFramer, downstreamStream, ct);
        var taskDown = PumpDownstreamToUpstreamAsync(downstreamFramer, upstreamStream, ct);

        try
        {
            var completedTask = await Task.WhenAny(taskUp, taskDown);
            string side = (completedTask == taskUp) ? "Upstream (CLS2Sim)" : "Downstream (MSFS)";
            logger.LogBridgeStopped($"{side} connection closed.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError($"Bridge Error: {ex.Message}");
        }
        finally
        {
            _cts.Cancel();
        }
    }

    private async Task PumpUpstreamToDownstreamAsync(SimConnectFramer framer, NetworkStream target, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var packet = await framer.ReadPacketAsync(ct);
            if (packet.IsEmpty) break;

            byte[] sharedBuffer = ArrayPool<byte>.Shared.Rent((int)packet.Length);
            try
            {
                packet.CopyTo(sharedBuffer);
                var span = sharedBuffer.AsSpan(0, (int)packet.Length);
                InspectClientPacket(span);
                await target.WriteAsync(sharedBuffer.AsMemory(0, (int)packet.Length), ct);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sharedBuffer);
            }

            await target.FlushAsync(ct);
            framer.AdvanceTo(packet.End);
        }
    }

    private async Task PumpDownstreamToUpstreamAsync(SimConnectFramer framer, NetworkStream target, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var packet = await framer.ReadPacketAsync(ct);
            if (packet.IsEmpty) break;

            byte[] sharedBuffer = ArrayPool<byte>.Shared.Rent((int)packet.Length);
            try
            {
                packet.CopyTo(sharedBuffer);
                var span = sharedBuffer.AsSpan(0, (int)packet.Length);
                int newLength = InspectSimPacket(span);
                await target.WriteAsync(sharedBuffer.AsMemory(0, newLength), ct);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sharedBuffer);
            }
            
            await target.FlushAsync(ct);
            framer.AdvanceTo(packet.End);
        }
    }

    /// <summary>
    /// Inspects and tracks state from client-to-simulator packets.
    /// </summary>
    private void InspectClientPacket(Span<byte> buffer)
    {
        if (buffer.Length < 12) return;
        
        uint dwId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(8));

        _clientPacketCounter++;
        switch ((Protocol.SendId)dwId)
                {
                    case Protocol.SendId.AddToDataDefinition:
                        if (buffer.Length >= 536)
                        {
                            uint defineId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12));
                            string originalName = Protocol.SimConnectParser.ReadString(buffer.Slice(20), 256).Trim();
                            string originalUnits = Protocol.SimConnectParser.ReadString(buffer.Slice(276), 256).Trim();
                            uint type = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(532));
                            
                            bool isRedirected = false;
                            string? mappedLVar = null;

                            if (mapper.TryGetRedirection(originalName, out mappedLVar) && mappedLVar != null)
                            {
                                isRedirected = true;
                                
                                logger.LogInfo($"[SendID={_clientPacketCounter}] BEFORE SUBSTITUTION HEX:\n{HexDump(buffer.Slice(0, 64))}");

                                // 1. OVERWRITE name in-place 
                                Span<byte> nameSlot = buffer.Slice(20, 252);
                                nameSlot.Clear();
                                System.Text.Encoding.ASCII.GetBytes(mappedLVar, nameSlot);
                                
                                // 2. FORCE units to "Number" (Standard SDK capitalization)
                                Span<byte> unitSlot = buffer.Slice(276, 256);
                                unitSlot.Clear();
                                System.Text.Encoding.ASCII.GetBytes("Number", unitSlot);
                                
                                logger.LogInfo($"[SendID={_clientPacketCounter}] NATIVE REDIRECTION: '{originalName}' ({originalUnits}) -> '{mappedLVar}' (Number)");
                                logger.LogInfo($"[SendID={_clientPacketCounter}] AFTER SUBSTITUTION HEX:\n{HexDump(buffer.Slice(0, 64))}");
                            }
                            else if (logger.IsEnabled(LogLevel.Debug))
                            {
                                logger.LogStringDebug($"[SendID={_clientPacketCounter}] Definition ID={defineId}: Name='{originalName}', Units='{originalUnits}'");
                            }
                            
                            stateTracker.AddVariableToDefinition(defineId, mappedLVar ?? originalName, isRedirected ? "Number" : originalUnits, type, isRedirected, originalName);
                        }
                        break;

            case Protocol.SendId.RequestDataOnSimObject:
                if (buffer.Length >= 20)
                {
                    uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12));
                    uint defineId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(16));
                    logger.LogClientPacket(dwId, $"RequestData: Req={requestId} Def={defineId}", (uint)buffer.Length);
                    stateTracker.MapRequest(requestId, defineId);
                }
                break;
        }
    }

    /// <summary>
    /// Inspects and potentially patches simulator-to-client packets in-place.
    /// </summary>
    /// <returns>The effective length of the packet (which may change due to normalization).</returns>
    private int InspectSimPacket(Span<byte> buffer)
    {
        if (buffer.Length < 12) return buffer.Length;
        
        uint dwId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(8));

        // Targeted aircraft detection based on configuration path (IDs 5, 8, 15)
        if (dwId == 5 || dwId == 8 || dwId == 15)
        {
            // Zero-allocation scan (Rule 1)
            // Using case-insensitive check because simulator paths differ (e.g., AIRCRAFT.CFG vs aircraft.cfg)
            if (ContainsAsciiCaseInsensitive(buffer, "aircraft.cfg"u8))
            {
                bool isComanche = ContainsAsciiCaseInsensitive(buffer, "pa24-250"u8);
                
                if (isComanche != stateTracker.IsComancheMode)
                {
                    stateTracker.IsComancheMode = isComanche;
                    logger.LogInfo(isComanche 
                        ? "Comanche detected via aircraft.cfg path. Enabling transformations." 
                        : "Non-Comanche aircraft detected. Disabling transformations.");
                }
            }
        }

        if (dwId == 1) // Exception
        {
             if (buffer.Length >= 24)
             {
                 uint exceptionCode = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12));
                 uint sendId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(16));
                 uint index = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(20));
                 logger.LogServerPacket(dwId, $"EXCEPTION DETAILS: Code={exceptionCode} SendID={sendId} Index={index}", (uint)buffer.Length);
             }
        }
        else if (dwId == 8 || dwId == 9) // SimObjectData (8) or SimObjectDataByType (9)
        {
            uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12));
            var definition = stateTracker.GetDefinitionForRequest(requestId);
            
            if (definition != null && stateTracker.IsComancheMode)
            {
                // SIMCONNECT_RECV_SIMOBJECT_DATA has data starting at 40 bytes.
                // Header(12) + Request(4) + Object(4) + Define(4) + Flags(4) + entry(4) + outof(4) + count(4) = 40
                if (buffer.Length >= 40)
                {
                    // 1. Normalize the payload (casts 8-byte L-Vars back to 4-byte expected slots)
                    // This also handles passive monitoring and logging.
                    int newPayloadSize = transformationEngine.NormalizePayload(buffer.Slice(40), definition);
                    int newTotalSize = 40 + newPayloadSize;
                    
                    // 2. Update the packet header with the new total size
                    BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)newTotalSize);
                    return newTotalSize;
                }
            }
        }
        
        return buffer.Length;
    }

    private static string HexDump(ReadOnlySpan<byte> data)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < data.Length; i++)
        {
            sb.Append(data[i].ToString("X2")).Append(' ');
            if ((i + 1) % 16 == 0) sb.AppendLine();
        }
        return sb.ToString();
    }

    private static bool ContainsAsciiCaseInsensitive(ReadOnlySpan<byte> source, ReadOnlySpan<byte> pattern)
    {
        if (pattern.Length == 0) return true;
        if (source.Length < pattern.Length) return false;

        for (int i = 0; i <= source.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                byte s = source[i + j];
                byte p = pattern[j];

                if (s != p)
                {
                    // Case insensitive ASCII check: convert BOTH to lower for comparison
                    byte sLow = (s >= 65 && s <= 90) ? (byte)(s + 32) : s;
                    byte pLow = (p >= 65 && p <= 90) ? (byte)(p + 32) : p;
                    if (sLow != pLow)
                    {
                        match = false;
                        break;
                    }
                }
            }
            if (match) return true;
        }
        return false;
    }
}

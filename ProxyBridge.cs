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
    TransformationEngine transformationEngine)
{
    private readonly CancellationTokenSource _cts = new();

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

            InspectClientPacket(packet);

            foreach (var segment in packet)
            {
                await target.WriteAsync(segment, ct);
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

            // 1. Always observe the packet (for title detection)
            InspectSimPacket(packet);

            // 2. Patch if needed
            if (stateTracker.IsComancheMode)
            {
                byte[] sharedBuffer = ArrayPool<byte>.Shared.Rent((int)packet.Length);
                try
                {
                    packet.CopyTo(sharedBuffer);
                    var span = sharedBuffer.AsSpan(0, (int)packet.Length);
                    
                    PatchBuffer(span);

                    await target.WriteAsync(sharedBuffer.AsMemory(0, (int)packet.Length), ct);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(sharedBuffer);
                }
            }
            else
            {
                foreach (var segment in packet)
                {
                    await target.WriteAsync(segment, ct);
                }
            }
            
            await target.FlushAsync(ct);
            framer.AdvanceTo(packet.End);
        }
    }

    private void InspectClientPacket(ReadOnlySequence<byte> packet)
    {
        if (packet.Length < 12) return;
        
        Span<byte> buffer = stackalloc byte[(int)Math.Min(packet.Length, 512)];
        packet.Slice(0, buffer.Length).CopyTo(buffer);
        
        uint dwId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(8));

        switch (dwId)
        {
            case 11: // AddToDataDefinition
                if (buffer.Length >= 20)
                {
                    uint defineId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12));
                    string name = Protocol.SimConnectParser.ReadString(buffer.Slice(16), 256);
                    stateTracker.AddVariableToDefinition(defineId, name, "Value", 0);
                }
                break;

            case 12: // RequestDataOnSimObject
                if (buffer.Length >= 20)
                {
                    uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12));
                    uint defineId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(16));
                    stateTracker.MapRequest(requestId, defineId);
                }
                break;
        }
    }

    private void PatchBuffer(Span<byte> buffer)
    {
        uint dwId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(8));

        if (dwId == 8) // SimObjectData
        {
            uint dwSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0));
            if (dwSize != buffer.Length) return;

            uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12));
            var definition = stateTracker.GetDefinitionForRequest(requestId);
            
            if (definition != null)
            {
                transformationEngine.PatchPayload(buffer.Slice(12), definition);
            }
        }
    }

    private void InspectSimPacket(ReadOnlySequence<byte> packet)
    {
        if (packet.Length < 24) return;

        Span<byte> buffer = stackalloc byte[512]; // Sufficient for header + some data
        int readLen = (int)Math.Min(packet.Length, 512);
        packet.Slice(0, readLen).CopyTo(buffer);
        
        uint dwId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(8));

        if (dwId == 8) // SimObjectData
        {
            uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12));
            var definition = stateTracker.GetDefinitionForRequest(requestId);
            
            if (definition != null && definition.Variables.Any(v => v.Name.Contains("TITLE", StringComparison.OrdinalIgnoreCase)))
            {
                stateTracker.UpdateAircraftTitle(buffer.Slice(24, readLen - 24));
            }
        }
    }
}

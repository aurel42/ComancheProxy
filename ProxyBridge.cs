using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;
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
    SidecarInjector sidecarInjector)
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _downstreamWriteLock = new(1, 1);
    private long _clientPacketCounter = 0;
    private uint _capturedDwVersion;
    private bool _sidecarInjected;

    public async Task RunAsync(CancellationToken externalCt)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt, _cts.Token);
        var ct = linkedCts.Token;

        string endpoints = $"{clientSocket.Client.RemoteEndPoint} <-> {simSocket.Client.RemoteEndPoint}";
        logger.LogBridgeStarted(endpoints);

        var upstreamStream = clientSocket.GetStream();
        var downstreamStream = simSocket.GetStream();

        await using var upstreamFramer = new SimConnectFramer(upstreamStream);
        await using var downstreamFramer = new SimConnectFramer(downstreamStream);

        var taskUp = PumpUpstreamToDownstreamAsync(upstreamFramer, downstreamStream, ct);
        var taskDown = PumpDownstreamToUpstreamAsync(downstreamFramer, downstreamStream, upstreamStream, ct);

        try
        {
            var completedTask = await Task.WhenAny(taskUp, taskDown);
            string side = (completedTask == taskUp) ? "Upstream (CLS2Sim)" : "Downstream (MSFS)";
            if (completedTask.IsFaulted)
            {
                logger.LogError($"{side} pump FAULTED: {completedTask.Exception?.InnerException?.Message}");
            }
            else
            {
                logger.LogBridgeStopped($"{side} connection closed.");
            }

            // Cancel the other pump, then observe both tasks to prevent unobserved exceptions
            _cts.Cancel();
            try { await Task.WhenAll(taskUp, taskDown); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { logger.LogError($"Pump cleanup error: {ex.Message}"); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError($"Bridge Error: {ex.Message}");
        }
        finally
        {
            _cts.Cancel();
            _cts.Dispose();
            _downstreamWriteLock.Dispose();
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
                bool forward = InspectClientPacket(span);

                if (forward)
                {
                    await _downstreamWriteLock.WaitAsync(ct);
                    try
                    {
                        await target.WriteAsync(sharedBuffer.AsMemory(0, (int)packet.Length), ct);
                        await target.FlushAsync(ct);
                    }
                    finally
                    {
                        _downstreamWriteLock.Release();
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sharedBuffer);
            }

            framer.AdvanceTo(packet.End);
        }
    }

    private async Task PumpDownstreamToUpstreamAsync(
        SimConnectFramer framer, NetworkStream downstreamStream, NetworkStream upstreamTarget, CancellationToken ct)
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

                var action = InspectSimPacket(span);

                // Handle sidecar injection/cleanup before forwarding
                if (action == SidecarAction.Inject)
                {
                    await InjectSidecarAsync(downstreamStream, ct);
                }
                else if (action == SidecarAction.Cleanup)
                {
                    await CleanupSidecarAsync(downstreamStream, ct);
                }

                // Drop sidecar responses — don't forward to CLS2Sim
                if (action != SidecarAction.Drop)
                {
                    await upstreamTarget.WriteAsync(sharedBuffer.AsMemory(0, (int)packet.Length), ct);
                    await upstreamTarget.FlushAsync(ct);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sharedBuffer);
            }

            framer.AdvanceTo(packet.End);
        }
    }

    /// <summary>
    /// Inspects and tracks state from client-to-simulator packets.
    /// Captures dwVersion from the first client packet for sidecar injection.
    /// Returns false to suppress the packet (do not forward to sim).
    /// </summary>
    private bool InspectClientPacket(Span<byte> buffer)
    {
        if (buffer.Length < 12) return true;

        // Capture dwVersion from the first client packet
        if (_capturedDwVersion == 0 && buffer.Length >= 8)
        {
            _capturedDwVersion = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(4));
            logger.LogInfo($"Captured dwVersion={_capturedDwVersion} from first client packet");
        }

        uint dwId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(8));

        _clientPacketCounter++;
        switch ((Protocol.SendId)(dwId & 0xFF))
        {
            case Protocol.SendId.AddToDataDefinition:
                if (buffer.Length >= 536)
                {
                    // Offset 12 is dwSendID (sequence), 16 is DefineID.
                    // DatumName/UnitsName/DatumType stay at 20/276/532 (unchanged).
                    uint defineId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(16));
                    string varName = Protocol.SimConnectParser.ReadString(buffer.Slice(20), 256).Trim();
                    string varUnits = Protocol.SimConnectParser.ReadString(buffer.Slice(276), 256).Trim();
                    uint type = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(532));

                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogStringDebug($"[SendID={_clientPacketCounter}] Definition ID={defineId}: Name='{varName}', Units='{varUnits}'");
                    }

                    stateTracker.AddVariableToDefinition(defineId, varName, varUnits, type);
                }
                break;

            case Protocol.SendId.RequestDataOnSimObject:
                if (buffer.Length >= 24)
                {
                    // Offset 12 is dwSendID (sequence number), payload starts at 16
                    uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(16));
                    uint defineId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(20));
                    logger.LogClientPacket(dwId, $"RequestData: Req={requestId} Def={defineId}", (uint)buffer.Length);
                    stateTracker.MapRequest(requestId, defineId);
                }
                break;

            case Protocol.SendId.MapClientEventToSimEvent:
                if (buffer.Length >= 20)
                {
                    uint eventId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(16));
                    string eventName = buffer.Length >= 276
                        ? Protocol.SimConnectParser.ReadString(buffer.Slice(20), 256).Trim()
                        : "(truncated)";
                    stateTracker.MapEvent(eventId, eventName);
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogClientEventMapping(eventId, eventName);
                    }
                }
                break;

            case Protocol.SendId.TransmitClientEvent:
                if (buffer.Length >= 32)
                {
                    uint objectId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(16));
                    uint eventId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(20));
                    uint data = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(28));
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        stateTracker.TryGetEventName(eventId, out string? eventName);
                        logger.LogClientEvent(eventId, eventName ?? $"(unmapped:{eventId})", data);
                    }
                }
                break;

            case Protocol.SendId.SetDataOnSimObject:
                if (buffer.Length >= 28)
                {
                    uint defineId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(16));
                    uint objectId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(20));
                    uint dataSize = (uint)buffer.Length - 36; // payload after 36-byte header
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        string variables = stateTracker.GetDefinitionSummary(defineId);
                        logger.LogClientSetData(defineId, variables, dataSize);
                    }
                }
                break;
        }

        return true;
    }

    /// <summary>
    /// Inspects and potentially patches simulator-to-client packets in-place.
    /// Detects Comanche mode transitions, sidecar responses, and applies overrides.
    /// </summary>
    private SidecarAction InspectSimPacket(Span<byte> buffer)
    {
        if (buffer.Length < 12) return SidecarAction.None;

        uint dwId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(8));
        var sidecarAction = SidecarAction.None;

        // Targeted aircraft detection based on configuration path (IDs 5, 8, 15)
        if (dwId == 5 || dwId == 8 || dwId == 15)
        {
            // Zero-allocation scan (Rule 1)
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

                // Inject sidecar if Comanche is active but not yet injected on this connection
                if (isComanche && !_sidecarInjected && _capturedDwVersion != 0)
                {
                    sidecarAction = SidecarAction.Inject;
                }
                else if (!isComanche && _sidecarInjected)
                {
                    sidecarAction = SidecarAction.Cleanup;
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
            if (buffer.Length >= 40)
            {
                uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12));

                // Check if this is our sidecar response
                if (SidecarInjector.IsSidecarResponse(requestId))
                {
                    ReadOnlySpan<byte> dataBlock = buffer.Slice(40);
                    bool stateChanged = sidecarInjector.ProcessResponse(dataBlock);

                    if (stateChanged)
                    {
                        logger.LogSidecarApState(sidecarInjector.IsAutopilotActive ? "ACTIVE" : "INACTIVE");
                    }

                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        sidecarInjector.TryGetOverrideValue("PROP THRUST:1", out double thrust);
                        logger.LogSidecarValues(
                            sidecarInjector.IsAutopilotActive ? "ON" : "OFF",
                            thrust);
                    }

                    // Drop this packet — don't forward to CLS2Sim
                    return SidecarAction.Drop;
                }

                // Normal client data — apply overrides
                var definition = stateTracker.GetDefinitionForRequest(requestId);

                if (definition != null && stateTracker.IsComancheMode)
                {
                    transformationEngine.NormalizePayload(
                        buffer.Slice(40), definition, stateTracker.IsComancheMode);
                }
            }
        }

        return sidecarAction;
    }

    /// <summary>
    /// Injects sidecar subscription packets into the MSFS stream.
    /// </summary>
    private async ValueTask InjectSidecarAsync(NetworkStream downstreamStream, CancellationToken ct)
    {
        if (_sidecarInjected) return;

        byte[] packets = sidecarInjector.BuildInjectionPackets(_capturedDwVersion);

        await _downstreamWriteLock.WaitAsync(ct);
        try
        {
            await downstreamStream.WriteAsync(packets.AsMemory(), ct);
            await downstreamStream.FlushAsync(ct);
            _sidecarInjected = true;
            logger.LogSidecarInjected(packets.Length);
        }
        finally
        {
            _downstreamWriteLock.Release();
        }
    }

    /// <summary>
    /// Sends a ClearDataDefinition packet to remove the sidecar subscription.
    /// </summary>
    private async ValueTask CleanupSidecarAsync(NetworkStream downstreamStream, CancellationToken ct)
    {
        if (!_sidecarInjected) return;

        byte[] packet = sidecarInjector.BuildCleanupPacket(_capturedDwVersion);

        await _downstreamWriteLock.WaitAsync(ct);
        try
        {
            await downstreamStream.WriteAsync(packet.AsMemory(), ct);
            await downstreamStream.FlushAsync(ct);
            _sidecarInjected = false;
            sidecarInjector.IsAutopilotActive = false;
            logger.LogSidecarCleanup();
        }
        finally
        {
            _downstreamWriteLock.Release();
        }
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

    /// <summary>
    /// Actions the downstream pump can take after inspecting a sim packet.
    /// </summary>
    private enum SidecarAction
    {
        /// <summary>No special action — forward packet normally.</summary>
        None,
        /// <summary>Inject sidecar subscription packets into the MSFS stream.</summary>
        Inject,
        /// <summary>Send ClearDataDefinition to remove the sidecar subscription.</summary>
        Cleanup,
        /// <summary>Drop this packet — do not forward to CLS2Sim.</summary>
        Drop
    }
}

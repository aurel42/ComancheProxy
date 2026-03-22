using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using ComancheProxy.Models;
using ComancheProxy.Protocol;

namespace ComancheProxy.Tcp;

/// <summary>
/// Discovers the MSFS SimConnect TCP port by enumerating process listeners
/// and probing candidates with a SimConnect handshake.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SimPortDiscovery(ProxyConfig config, ProxyLogger logger)
{
    /// <summary>
    /// IPv4 address for 127.0.0.1 in network byte order.
    /// </summary>
    private const uint LoopbackAddr = 0x0100007F;

    /// <summary>
    /// IPv4 address for 0.0.0.0 in network byte order.
    /// </summary>
    private const uint AnyAddr = 0x00000000;

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(1000);
    private readonly ConcurrentDictionary<int, int> _failCounts = new();

    /// <summary>
    /// Discovers the SimConnect port by enumerating MSFS listeners, filtering candidates,
    /// and probing each with a SimConnect handshake. Returns <c>null</c> if no port is found.
    /// </summary>
    public async ValueTask<int?> DiscoverPortAsync(CancellationToken ct)
    {
        var pids = FindMsfsPids();
        if (pids.Count == 0)
        {
            logger.LogDiscoveryInfo($"no '{config.MSFSProcessName}' process found");
            return null;
        }

        var excludedPorts = new HashSet<int>(config.ExcludedPorts);
        if (config.CLS2SimPort > 0) excludedPorts.Add(config.CLS2SimPort);
        if (config.FsePort > 0) excludedPorts.Add(config.FsePort);

        var candidates = GetListeners()
            .Where(l => pids.Contains(l.Pid))
            .Where(l => l.LocalAddr is LoopbackAddr or AnyAddr)
            .Where(l => !excludedPorts.Contains(l.Port))
            .Select(l => l.Port)
            .Distinct()
            .ToList();

        if (candidates.Count == 0)
        {
            logger.LogDiscoveryInfo("no candidate ports after filtering");
            return null;
        }

        logger.LogDiscoveryInfo($"{candidates.Count} candidate(s): [{string.Join(", ", candidates)}]");

        // If the configured port is among candidates, use it directly — no probing needed
        if (candidates.Contains(config.MSFSPort))
        {
            logger.LogDiscoveredPort(config.MSFSPort);
            return config.MSFSPort;
        }

        // Single candidate during startup is likely the crash handler, not SimConnect
        if (candidates.Count == 1)
        {
            logger.LogDiscoveryInfo($"single candidate {candidates[0]}, skipping probe");
            return null;
        }

        // Multiple candidates, configured port not among them — probe ascending, stop on first match
        candidates.Sort();

        var probeCandidates = candidates.Where(p => !_failCounts.TryGetValue(p, out int c) || c < 2).ToList();
        if (probeCandidates.Count == 0)
        {
            logger.LogDiscoveryInfo("all candidates previously failed, resetting ignore list");
            _failCounts.Clear();
            probeCandidates = candidates;
        }

        foreach (int port in probeCandidates)
        {
            if (ct.IsCancellationRequested) break;

            if (await ProbeSimConnectAsync(port, ct))
            {
                _failCounts.TryRemove(port, out _);
                logger.LogDiscoveredPort(port);
                return port;
            }

            _failCounts.AddOrUpdate(port, 1, (_, count) => count + 1);
        }

        logger.LogDiscoveryInfo("all candidates failed handshake probe");
        return null;
    }

    /// <summary>
    /// Probes a single port with a SimConnect Open handshake.
    /// Returns true if the port responds with a valid SimConnect packet.
    /// </summary>
    private async ValueTask<bool> ProbeSimConnectAsync(int port, CancellationToken ct)
    {
        logger.LogProbeAttempt(port);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProbeTimeout);
        var token = cts.Token;

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port, token);

            var stream = client.GetStream();

            // Send a SimConnect Open packet
            byte[] openPacket = BuildProbeOpenPacket();
            await stream.WriteAsync(openPacket, token);

            // Read the 12-byte server→client header
            byte[] header = new byte[12];
            int totalRead = 0;
            while (totalRead < 12)
            {
                int read = await stream.ReadAsync(header.AsMemory(totalRead, 12 - totalRead), token);
                if (read == 0)
                {
                    logger.LogProbeFailure(port, "connection closed before header");
                    return false;
                }
                totalRead += read;
            }

            uint dwSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
            uint dwId = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4));

            // Validate: exact SimConnect signatures only
            bool isRecvOpen = dwSize == 308 && dwId == (uint)RecvId.Open;
            bool isRecvException = dwSize == 24 && dwId == (uint)RecvId.Exception;
            if (!isRecvOpen && !isRecvException)
            {
                logger.LogProbeFailure(port, $"not SimConnect (dwSize={dwSize}, dwID={dwId})");
                return false;
            }

            logger.LogProbeSuccess(port, dwId);

            // Clean up: send Close packet so MSFS doesn't retain a phantom client
            await TrySendCloseAsync(stream, token);

            return true;
        }
        catch (OperationCanceledException)
        {
            logger.LogProbeFailure(port, "timeout");
            return false;
        }
        catch (SocketException ex)
        {
            logger.LogProbeFailure(port, ex.Message);
            return false;
        }
        catch (IOException ex)
        {
            logger.LogProbeFailure(port, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Builds a minimal SimConnect Open packet (SendId 1) for probing.
    /// Layout: 16-byte client header + 256-byte application name = 272 bytes.
    /// </summary>
    private static byte[] BuildProbeOpenPacket()
    {
        const int packetSize = 272;
        byte[] packet = new byte[packetSize];
        var span = packet.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span, packetSize);           // dwSize
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), 3);           // dwVersion (matches CLS2Sim)
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), SendId.Open.Wire()); // dwID
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(12), 0);          // dwSendID

        Encoding.ASCII.GetBytes("ComancheProbe", span.Slice(16, 256));

        return packet;
    }

    /// <summary>
    /// Best-effort Close packet to clean up the probe session.
    /// </summary>
    private static async ValueTask TrySendCloseAsync(NetworkStream stream, CancellationToken ct)
    {
        try
        {
            byte[] close = new byte[20];
            var span = close.AsSpan();
            BinaryPrimitives.WriteUInt32LittleEndian(span, 20);                    // dwSize
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), SendId.Close.Wire()); // dwID
            await stream.WriteAsync(close, ct);
        }
        catch
        {
            // Best effort — ignore failures
        }
    }

    private HashSet<int> FindMsfsPids()
    {
        var processes = Process.GetProcesses();
        try
        {
            return processes
                .Where(p => p.ProcessName.Equals(config.MSFSProcessName, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Id)
                .ToHashSet();
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    private record TcpListenerInfo(int Port, uint LocalAddr, int Pid);

    private static List<TcpListenerInfo> GetListeners()
    {
        var results = new List<TcpListenerInfo>();
        int bufferSize = 0;
        uint ret = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, false, 2, TcpTableClass.TcpTableOwnerPidListener);

        IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufferSize);
        try
        {
            ret = GetExtendedTcpTable(tcpTablePtr, ref bufferSize, false, 2, TcpTableClass.TcpTableOwnerPidListener);
            if (ret != 0) return results;

            int rowCount = Marshal.ReadInt32(tcpTablePtr);
            IntPtr rowPtr = tcpTablePtr + 4;

            for (int i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                int port = ((row.localPort & 0xff) << 8) | ((row.localPort & 0xff00) >> 8);
                results.Add(new TcpListenerInfo(port, row.localAddr, row.owningPid));
                rowPtr += Marshal.SizeOf<MibTcpRowOwnerPid>();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(tcpTablePtr);
        }

        return results;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, TcpTableClass tblClass, uint reserved = 0);

    private enum TcpTableClass
    {
        TcpTableBasicListener,
        TcpTableBasicConnections,
        TcpTableBasicAll,
        TcpTableOwnerPidListener,
        TcpTableOwnerPidConnections,
        TcpTableOwnerPidAll,
        TcpTableOwnerModuleListener,
        TcpTableOwnerModuleConnections,
        TcpTableOwnerModuleAll
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint state;
        public uint localAddr;
        public int localPort; // network byte order
        public uint remoteAddr;
        public uint remotePort;
        public int owningPid;
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ComancheProxy.Tcp;

/// <summary>
/// Discovers the MSFS SimConnect TCP port by inspecting FlightSimulator process listeners.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SimPortDiscovery
{
    private static readonly int[] PriorityPorts = [500, 5001];
    private static readonly (int start, int end)[] PortRanges = [(5000, 5100), (15000, 15050)];

    /// <summary>
    /// Scans for a running FlightSimulator process and returns the first SimConnect
    /// listening port found, ordered by priority ports then known ranges.
    /// Returns <c>null</c> if no FlightSimulator process or matching port is found.
    /// </summary>
    public static int? DiscoverPort()
    {
        var processes = Process.GetProcesses();
        HashSet<int> pids;
        try
        {
            pids = processes
                .Where(p => p.ProcessName.StartsWith("FlightSimulator", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Id)
                .ToHashSet();
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }

        if (pids.Count == 0) return null;

        var listeners = GetListeners();
        var msfsPorts = listeners
            .Where(l => pids.Contains(l.Pid))
            .Select(l => l.Port)
            .ToHashSet();

        if (msfsPorts.Count == 0) return null;

        // 1. Check priority ports in order
        foreach (var port in PriorityPorts)
        {
            if (msfsPorts.Contains(port)) return port;
        }

        // 2. Check known ranges in order, lowest port first
        foreach (var (start, end) in PortRanges)
        {
            for (int port = start; port <= end; port++)
            {
                if (msfsPorts.Contains(port)) return port;
            }
        }

        return null;
    }

    private record TcpListenerInfo(int Port, int Pid);

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
                // Port is in network byte order (big endian)
                int port = ((row.localPort & 0xff) << 8) | ((row.localPort & 0xff00) >> 8);
                results.Add(new TcpListenerInfo(port, row.owningPid));
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

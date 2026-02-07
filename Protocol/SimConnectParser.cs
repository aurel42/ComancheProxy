using System.Buffers.Binary;
using System.Text;

namespace ComancheProxy.Protocol;

/// <summary>
/// Helper for safe, zero-allocation parsing of SimConnect binary packets.
/// </summary>
public static class SimConnectParser
{
    public static string ReadString(ReadOnlySpan<byte> data, int maxLength)
    {
        int length = data.IndexOf((byte)0);
        if (length < 0 || length > maxLength) length = maxLength;
        return Encoding.ASCII.GetString(data.Slice(0, length));
    }

    public static uint ReadUInt32(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt32LittleEndian(data);
}

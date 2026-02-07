using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace ComancheProxy.Tcp;

/// <summary>
/// Handles SimConnect binary protocol framing over a byte stream.
/// </summary>
public sealed class SimConnectFramer(NetworkStream networkStream)
{
    private readonly PipeReader _reader = PipeReader.Create(networkStream);

    /// <summary>
    /// Reads the next complete SimConnect packet from the stream.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A ReadOnlySequence containing the full packet (header + payload).</returns>
    public async ValueTask<ReadOnlySequence<byte>> ReadPacketAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            ReadResult result = await _reader.ReadAsync(ct);
            ReadOnlySequence<byte> buffer = result.Buffer;

            if (TryParsePacket(ref buffer, out ReadOnlySequence<byte> packet))
            {
                // We return the packet WITHOUT advancing the internal reader yet.
                // The caller MUST call AdvanceTo(packet.End) after they are done with the sequence.
                return packet;
            }

            // Tell the reader we've examined everything but consumed nothing.
            _reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted) break;
        }

        return ReadOnlySequence<byte>.Empty;
    }

    private static bool TryParsePacket(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> packet)
    {
        packet = default;

        if (buffer.Length < 4)
        {
            return false;
        }

        // SimConnect Header dwSize is the first 4 bytes (UInt32, Little-Endian)
        Span<byte> sizeHeader = stackalloc byte[4];
        buffer.Slice(0, 4).CopyTo(sizeHeader);
        uint dwSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(sizeHeader);

        if (dwSize < 12 || dwSize > 1024 * 1024) // Sanity check: 12 bytes min, 1MB max
        {
            // Corrupted stream or invalid data
            throw new InvalidDataException($"Invalid SimConnect packet size: {dwSize}");
        }

        if (buffer.Length < dwSize)
        {
            return false;
        }

        packet = buffer.Slice(0, dwSize);
        buffer = buffer.Slice(dwSize);
        return true;
    }

    /// <summary>
    /// Signals that the packet has been processed and can be removed from the reader.
    /// </summary>
    public void AdvanceTo(SequencePosition consumed)
    {
        _reader.AdvanceTo(consumed);
    }
}

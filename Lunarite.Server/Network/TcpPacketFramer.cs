using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Lunarite.Network;

// CClientTcpLink frames are prefixed by a little-endian ushort whose value includes the prefix.
// NetworkStream reads have no packet boundaries, so retain incomplete tails across callbacks.
public sealed class TcpPacketFramer
{
    public const int MaximumPacketLength = 2047;
    public const int MaximumServerPacketLength = 4095;
    private readonly List<byte> _pending = [];
    private readonly int _minimumPacketLength;
    private readonly int _maximumPacketLength;

    public TcpPacketFramer(int minimumPacketLength = 4, int maximumPacketLength = MaximumPacketLength)
    {
        _minimumPacketLength = minimumPacketLength;
        _maximumPacketLength = maximumPacketLength;
    }

    public IReadOnlyList<byte[]> Append(ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
            _pending.Add(bytes[i]);

        var frames = new List<byte[]>();
        while (_pending.Count >= sizeof(ushort))
        {
            ushort length = BinaryPrimitives.ReadUInt16LittleEndian([
                _pending[0], _pending[1]
            ]);
            if (length < _minimumPacketLength || length > _maximumPacketLength)
                throw new InvalidOperationException($"Invalid TCP packet length {length}.");
            if (_pending.Count < length)
                break;

            byte[] frame = _pending.GetRange(0, length).ToArray();
            _pending.RemoveRange(0, length);
            frames.Add(frame);
        }
        return frames;
    }
}

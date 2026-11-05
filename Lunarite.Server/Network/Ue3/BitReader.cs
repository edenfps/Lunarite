using System;

namespace Lunarite.Network.Ue3;

// Unreal Engine 3 (build ~3240) FBitReader: LSB-first bit reading, mirror of BitWriter.
// The true bit length of a received net packet is recovered by scanning the last byte
// for the terminating "1" stop bit (see FromPacket).
public sealed class BitReader
{
    private readonly byte[] _buf;
    private int _pos;       // bit position
    private readonly int _numBits;

    public bool Overflowed { get; private set; }

    public BitReader(byte[] data, int numBits)
    {
        _buf = data;
        _numBits = numBits;
    }

    // Build a reader from a received net packet: strip the terminating stop bit so the
    // bunch loop ends exactly. Returns null if the packet is all-zero (no stop bit).
    public static BitReader? FromPacket(ReadOnlySpan<byte> payload)
    {
        int totalBits = payload.Length * 8;
        // Find the highest set bit = the stop bit; real data length is one less.
        int last = totalBits - 1;
        while (last >= 0 && (payload[last >> 3] & (1 << (last & 7))) == 0)
            last--;
        if (last < 0)
            return null;
        return new BitReader(payload.ToArray(), last);
    }

    public int Position => _pos;
    public int NumBits => _numBits;
    public bool AtEnd => _pos >= _numBits;

    public bool ReadBit()
    {
        if (_pos >= _numBits) { Overflowed = true; return false; }
        bool bit = (_buf[_pos >> 3] & (1 << (_pos & 7))) != 0;
        _pos++;
        return bit;
    }

    // FBitReader::ReadInt(Max)
    public uint ReadInt(uint max)
    {
        uint value = 0;
        for (uint mask = 1; value + mask < max && mask != 0 && !Overflowed; mask <<= 1)
            if (ReadBit()) value |= mask;
        return value;
    }

    // Alias of ReadInt: in UE3 (UnBits.cpp, build 3340) FBitReader::ReadInt is the same
    // value-aware SerializeInt.
    public uint ReadIntValueAware(uint max) => ReadInt(max);

    public uint ReadUInt32() { uint v = 0; for (int i = 0; i < 32; i++) if (ReadBit()) v |= 1u << i; return v; }
    public int ReadInt32() => (int)ReadUInt32();
    public byte ReadByte() { byte b = 0; for (int i = 0; i < 8; i++) if (ReadBit()) b |= (byte)(1 << i); return b; }

    // Copies `count` bits from absolute position `start` (LSB-first) without moving the cursor.
    public byte[] CopyBits(int start, int count)
    {
        if (count < 0) count = 0;
        var bytes = new byte[(count + 7) >> 3];
        for (int i = 0; i < count; i++)
        {
            int bitIndex = start + i;
            if (bitIndex < 0 || bitIndex >= _numBits) break;
            if ((_buf[bitIndex >> 3] & (1 << (bitIndex & 7))) != 0)
                bytes[i >> 3] |= (byte)(1 << (i & 7));
        }
        return bytes;
    }
}

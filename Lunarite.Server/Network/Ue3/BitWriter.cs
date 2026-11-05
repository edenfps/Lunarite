using System;
using System.Collections.Generic;

namespace Lunarite.Network.Ue3;

// Unreal Engine 3 (build ~3240) FBitWriter: LSB-first bit packing.
// Bit N is stored in Buffer[N>>3] at bit position (N & 7), where the bit value
// is (1 << (N & 7)). Bounded ints use UE3's value-aware SerializeInt loop.
//
// Net packets are byte-buffers whose true bit length is recovered by the receiver
// via a terminating "1" stop bit (Terminate()), then zero-padded to a byte.
public sealed class BitWriter
{
    private readonly List<byte> _buf = [];
    private int _numBits;

    public int NumBits => _numBits;
    public int NumBytes => (_numBits + 7) >> 3;

    public void WriteBit(bool bit)
    {
        int byteIndex = _numBits >> 3;
        while (_buf.Count <= byteIndex)
            _buf.Add(0);
        if (bit)
            _buf[byteIndex] |= (byte)(1 << (_numBits & 7));
        _numBits++;
    }

    // FBitWriter::WriteInt(Value, Max)
    public void WriteInt(uint value, uint max)
    {
        uint encoded = 0;
        for (uint mask = 1; encoded + mask < max && mask != 0; mask <<= 1)
        {
            bool set = (value & mask) != 0;
            WriteBit(set);
            if (set) encoded += mask;
        }
    }

    // Fixed-width helpers (32/16/8-bit little-endian bit order).
    public void WriteUInt32(uint v) { for (int i = 0; i < 32; i++) WriteBit((v & (1u << i)) != 0); }
    public void WriteInt32(int v) => WriteUInt32((uint)v);
    public void WriteByte(byte b) { for (int i = 0; i < 8; i++) WriteBit((b & (1 << i)) != 0); }

    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            WriteByte(b);
    }

    // Append the raw bits of another writer (bit-exact).
    public void WriteBitsFrom(BitWriter other)
    {
        var bytes = other._buf;
        for (int i = 0; i < other._numBits; i++)
            WriteBit((bytes[i >> 3] & (1 << (i & 7))) != 0);
    }

    // UE3 net string: FString serialized as INT length (incl. null term, negative
    // for UNICODE) then the chars. ANSI form: length = count+1, then ASCII bytes + 0.
    public void WriteString(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            WriteInt32(0);
            return;
        }
        WriteInt32(s.Length + 1);
        foreach (char c in s)
            WriteByte((byte)c);
        WriteByte(0);
    }

    // Terminating stop bit so the receiver can recover the exact bit length,
    // then pad to a byte boundary. Call once, right before ToArray() for a packet.
    public void Terminate()
    {
        WriteBit(true);
        while ((_numBits & 7) != 0)
            WriteBit(false);
    }

    public byte[] ToArray() => _buf.ToArray();
}

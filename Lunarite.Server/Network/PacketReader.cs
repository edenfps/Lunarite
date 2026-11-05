using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Lunarite.Network;

public ref struct PacketReader
{
    private int position;
    private readonly int length;
    private readonly ref byte reference;

    public PacketReader(ReadOnlySpan<byte> span)
    {
        length = span.Length;
        reference = ref MemoryMarshal.GetReference(span);
    }

    public readonly int RemainingLength => length - position;

    public readonly ReadOnlySpan<byte> Span => MemoryMarshal.CreateReadOnlySpan(ref reference, length);

    public readonly ReadOnlySpan<byte> ConsumedSpan => MemoryMarshal.CreateReadOnlySpan(ref reference, position);

    public readonly ReadOnlySpan<byte> RemainingSpan =>
        MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref reference, position), RemainingLength);

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)count, (uint)RemainingLength, nameof(count));

        position += count;
    }

    public void Rewind(int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)count, (uint)position, nameof(count));

        position -= count;
    }

    public void Reset() => position = 0;

    public ReadOnlySpan<byte> Read(int count)
    {
        if (!TryRead(count, out var result))
            throw new InternalBufferOverflowException();

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe T Read<T>() where T : unmanaged
    {
        return Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(Read(sizeof(T))));
    }

    private bool TryRead(int count, [MaybeNullWhen(false)] out ReadOnlySpan<byte> result)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var newLength = position + count;

        if ((uint)newLength <= (uint)length)
        {
            result = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref reference, position), count);
            position = newLength;
            return true;
        }

        result = default;

        return false;
    }

    public unsafe bool TryRead<T>([MaybeNullWhen(false)] out T result) where T : unmanaged
    {
        if (MemoryMarshal.TryRead(RemainingSpan, out result))
        {
            Advance(sizeof(T));
            return true;
        }

        return false;
    }

    public bool TryReadExact(int count, [MaybeNullWhen(false)] out Span<byte> result)
    {
        if (count >= 0)
        {
            var span = RemainingSpan;

            if ((uint)count <= (uint)span.Length)
            {
                result = MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(span), count);

                Advance(count);

                return true;
            }
        }

        result = default;

        return false;
    }

    #region Helpers

    public bool TryRead([MaybeNullWhen(false)] out string? value)
    {
        value = default;

        var span = RemainingSpan;

        var nullIndex = span.IndexOf(byte.MinValue);

        if (nullIndex == -1)
            return false;

        var stringSequence = MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(span), nullIndex);

        // Client strings are MBCS in the player's Windows code page (CP949 on a Korean PC, CP1252 on a
        // US one). Latin-1 maps each byte to one char and back, so names and chat reach other clients
        // byte for byte, as the retail server's char buffers did; UTF-8 turned Korean into U+FFFD.
        value = Encoding.Latin1.GetString(stringSequence);

        Advance(nullIndex + 1);

        return true;
    }

    public bool TryRead([NotNullWhen(true)] out IPAddress? value, bool limited = false)
    {
        value = default;

        if (!TryReadExact(4, out var address))
            return false;

        value = new IPAddress(address);

        return true;
    }

    #endregion
}
using System;
using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ResetLicense : IPacketSerialize
{
    public byte Result;
    public int LicensePoints;
    public IReadOnlyList<int> Licenses = [];
    public byte[] Serialize()
    {
        using var w = new PacketWriter(); w.Write((byte)96); w.Write(Result);
        if (Result == 0)
        {
            // Native OnResetLicense reads "wb": a word LP and a byte license count.
            int count = Math.Min(byte.MaxValue, Licenses.Count);
            w.Write((ushort)Math.Clamp(LicensePoints, 0, ushort.MaxValue)); w.Write((byte)count);
            for (int i = 0; i < count; i++) w.Write(Licenses[i]);
        }
        return w.Buffer;
    }
}

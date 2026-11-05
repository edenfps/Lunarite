using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnProcLicense -> Unreal event OnProcLicense(bool Plus, byte Result, int License).
public sealed class ProcLicense : IPacketSerialize
{
    public bool Plus;
    public byte Result;
    public int License;
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)95); w.Write((byte)(Plus ? 1 : 0)); w.Write(Result); w.Write(License);
        return w.Buffer;
    }
}

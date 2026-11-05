using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native ProcNewOwnerBC (server op137) consumes bdd,s,bbb,dwdw. Besides the
// script-visible fields, retail refreshes the owner's cached P2P identity. The first byte is the
// native PingType: 1 registers the new owner (by PID) as a room P2P peer to probe, 2 registers
// the room ID instead, anything else registers nothing.
public sealed class NewOwnerBC : IPacketSerialize
{
    public byte PingType = 1;
    public int RoomId;
    public int Pid;
    public string Name = "";
    public byte Level;
    public byte PingGrade = 4;
    public byte FullConnection = 1;
    public int PublicAddress;
    public ushort PublicPort;
    public int PrivateAddress;
    public ushort PrivatePort;

    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)137); w.Write(PingType); w.Write(RoomId); w.Write(Pid); w.Write(Name);
        w.Write(Level); w.Write(PingGrade); w.Write(FullConnection);
        w.Write(PublicAddress); w.Write(PublicPort); w.Write(PrivateAddress); w.Write(PrivatePort);
        return w.Buffer;
    }
}

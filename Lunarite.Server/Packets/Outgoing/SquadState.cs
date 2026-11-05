using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server op98 is a one-byte squad-state bitmask. Server op100 begins the native
// ProcSquadMemberList payload with a member count; zero is the no-squad list.
public sealed class SquadState : IPacketSerialize
{
    public byte State;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)98); writer.Write(State);
        return writer.Buffer;
    }
}

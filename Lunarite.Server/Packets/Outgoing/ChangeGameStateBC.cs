using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ChangeGameStateBC : IPacketSerialize
{
    public byte RoomId;
    public byte GameState;
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)149); w.Write(RoomId); w.Write(GameState);
        return w.Buffer;
    }
}

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class DestroyRoomBC : IPacketSerialize
{
    public byte RoomId;
    public byte[] Serialize() => [(byte)146, RoomId];
}

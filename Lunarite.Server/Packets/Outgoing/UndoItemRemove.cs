using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class UndoItemRemove : IPacketSerialize
{
    public byte Serial;
    public byte[] Serialize() => [(byte)196, Serial];
}

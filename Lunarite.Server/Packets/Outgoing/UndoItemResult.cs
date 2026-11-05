using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class UndoItemResult : IPacketSerialize
{
    public byte Result;
    public byte[] Serialize() => [(byte)197, Result];
}

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class InsertLearnedRecipe : IPacketSerialize
{
    public int Recipe;
    public byte[] Serialize() { using var w = new PacketWriter(); w.Write((byte)93); w.Write(Recipe); return w.Buffer; }
}

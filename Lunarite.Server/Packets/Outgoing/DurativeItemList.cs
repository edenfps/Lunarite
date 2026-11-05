using System.Collections.Generic;

using Lunarite.Network;
using Lunarite.Packets.Common;

namespace Lunarite.Packets.Outgoing;

// Native op231 replaces the city player's complete DurativeItem array.
public sealed class DurativeItemList : IPacketSerialize
{
    public List<DurativeItem> Items = [];
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)231);
        writer.Write(Items.Count);
        foreach (var item in Items) item.Serialize(writer);
        return writer.Buffer;
    }
}

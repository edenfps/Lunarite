using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class RequestCraftRecipe : IPacketSerialize
{
    public IReadOnlyList<int> Recipes = [];
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)92); w.Write((ushort)Recipes.Count);
        foreach (int recipe in Recipes) w.Write(recipe);
        return w.Buffer;
    }
}

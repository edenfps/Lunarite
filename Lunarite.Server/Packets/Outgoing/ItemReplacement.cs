using System;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native op217 removes one addressed item, inserts the supplied full GenericItem,
// then refreshes the client's inventory view. Body: bbM[32].
public sealed class ItemReplacement : IPacketSerialize
{
    public byte RemovedInventory;
    public byte RemovedCell;
    public InsertItem Item = new();

    public byte[] Serialize()
    {
        byte[] insert = Item.Serialize();
        if (insert.Length != 33 || insert[0] != 82)
            throw new InvalidOperationException("Replacement GenericItem must be exactly 32 bytes.");

        using var writer = new PacketWriter();
        writer.Write((byte)217); writer.Write(RemovedInventory); writer.Write(RemovedCell);
        writer.Write(insert.AsSpan(1));
        return writer.Buffer;
    }
}

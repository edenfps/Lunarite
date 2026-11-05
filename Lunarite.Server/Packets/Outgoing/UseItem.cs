using System;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class UseItem : IPacketSerialize
{
    public byte Result;
    public byte UseType;
    public byte[]? ItemData;
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)79); w.Write(Result);
        if (Result == 0)
        {
            w.Write(UseType);
            // Native OnUseItem case 1 immediately parses one 32-byte GenericItem before
            // invoking the inventory replacement callback. Other known use types have no body.
            if (UseType == 1)
            {
                if (ItemData is not { Length: 32 })
                    throw new InvalidOperationException("spray use result requires one 32-byte GenericItem");
                w.Write(ItemData);
            }
        }
        return w.Buffer;
    }
}

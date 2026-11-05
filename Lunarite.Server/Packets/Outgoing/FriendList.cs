using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class FriendList : IPacketSerialize
{
    public byte Result = 5; // FRIEND_LIST_SUCCESS
    public List<FriendWireEntry> Entries { get; } = [];
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write((byte)160); writer.Write(Result);
        if (Result == 5)
        {
            writer.Write((byte)Entries.Count);
            foreach (var entry in Entries)
            {
                writer.Write(entry.Relation); writer.Write(entry.Pid); writer.Write(entry.Name);
                writer.Write(entry.Species); writer.Write(entry.Level); writer.Write(entry.Style); writer.Write(entry.Login);
            }
        }
        return writer.Buffer;
    }
}

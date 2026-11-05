using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class TransmitContent : IPacketSerialize
{
    public sealed record Attachment(int Sequence, int ItemId, int Count);
    public int MessageSequence;
    public byte Type;
    public string From = "";
    public int SenderPid;
    public string Title = "";
    public string Content = "";
    public int CR;
    public List<Attachment> Attachments = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)200);
        writer.Write(MessageSequence);
        writer.Write(Type);
        writer.Write(From);
        writer.Write(SenderPid);
        writer.Write(Title);
        writer.Write(Content);
        writer.Write(CR);
        // Three fixed attachment triples: sequence, item ID, count. Zero item ID = empty/claimed.
        for (int i = 0; i < 3; i++)
        {
            var item = i < Attachments.Count ? Attachments[i] : null;
            writer.Write(item?.Sequence ?? 0);
            writer.Write(item?.ItemId ?? 0);
            writer.Write(item?.Count ?? 0);
        }
        return writer.Buffer;
    }
}

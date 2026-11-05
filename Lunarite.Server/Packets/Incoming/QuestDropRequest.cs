using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

public sealed class QuestDropRequest
{
    public int Index;

    public static bool TryDeserialize(ref PacketReader reader, out QuestDropRequest packet)
    {
        packet = new QuestDropRequest();
        return reader.TryRead(out packet.Index) && reader.RemainingLength == 0;
    }
}

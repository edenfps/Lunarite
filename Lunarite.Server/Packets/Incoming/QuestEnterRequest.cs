using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// d
public class QuestEnterRequest : IPacketDeserialize<QuestEnterRequest>
{
    public int Key;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out QuestEnterRequest? packet)
    {
        packet = new QuestEnterRequest();

        if (!reader.TryRead(out packet.Key))
            return false;

        return reader.RemainingLength == 0;
    }

    public override string ToString() => $"Key={Key}";
}

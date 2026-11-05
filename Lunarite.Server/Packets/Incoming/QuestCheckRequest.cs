using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// db
public class QuestCheckRequest : IPacketDeserialize<QuestCheckRequest>
{
    public int Index;
    public byte DifficultyLevel;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out QuestCheckRequest? packet)
    {
        packet = new QuestCheckRequest();

        if (!reader.TryRead(out packet.Index))
            return false;

        if (!reader.TryRead(out packet.DifficultyLevel))
            return false;

        return reader.RemainingLength == 0;
    }

    public override string ToString() => $"Index={Index} DifficultyLevel={DifficultyLevel}";
}

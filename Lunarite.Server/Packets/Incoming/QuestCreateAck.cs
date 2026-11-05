using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// Client opcode 51, `db`.
// The op42 QuestCreate parser (sub_111C0920) emits this after it has installed the
// room data and travel inventory: QuestIndex followed by 1 for success.
public sealed class QuestCreateAck : IPacketDeserialize<QuestCreateAck>
{
    public int QuestIndex;
    public byte Ready;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out QuestCreateAck? packet)
    {
        packet = new QuestCreateAck();
        return reader.TryRead(out packet.QuestIndex)
            && reader.TryRead(out packet.Ready)
            && reader.RemainingLength == 0;
    }
}

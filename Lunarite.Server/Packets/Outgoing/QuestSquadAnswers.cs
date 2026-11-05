using System.Collections.Generic;
using System.Linq;
using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native op220/op221 both decode: b count, then count * (d PID, d quest, b answer).
public sealed class QuestSquadAnswers : IPacketSerialize
{
    public byte Opcode;
    public List<Entry> Answers = [];

    public readonly record struct Entry(int Pid, int QuestIndex, byte Answer);

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write(Opcode);
        int count = System.Math.Min(byte.MaxValue, Answers.Count);
        writer.Write((byte)count);
        foreach (var answer in Answers.Take(count))
        {
            writer.Write(answer.Pid);
            writer.Write(answer.QuestIndex);
            writer.Write(answer.Answer);
        }
        return writer.Buffer;
    }
}

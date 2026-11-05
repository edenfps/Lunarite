using System.Linq;

using Lunarite.Network;
using Lunarite.Packets.Common;

namespace Lunarite.Packets.Outgoing;

// b
// dsbdwbbdmm
public class CreatePlayer : IPacketSerialize
{
    public const int ArmorPartCount = 5;
    public byte Answer;

    public int Pid;

    public string? Name;

    public byte Index;

    public int Money;

    public short Level;

    public byte Style;
    public byte Rank;

    public int QT;

    public Custom Custom = new();

    public GenericItemBase[] ArmorList =
        Enumerable.Range(0, ArmorPartCount).Select(_ => new GenericItemBase()).ToArray();

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)12);

        writer.Write(Answer);

        if (Answer == 0)
        {
            writer.Write(Pid);

            writer.Write(Name);

            writer.Write(Index);

            writer.Write(Money);

            writer.Write(Level);
            writer.Write(Style);

            writer.Write(Rank);

            writer.Write(QT);

            Custom.Serialize(writer);

            for (var i = 0; i < ArmorPartCount; i++)
            {
                var armor = i < ArmorList.Length ? ArmorList[i] : null;
                (armor ?? new GenericItemBase()).Serialize(writer);
            }
        }

        return writer.Buffer;
    }
}

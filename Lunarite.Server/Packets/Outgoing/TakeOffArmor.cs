using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server op71. Native OnTakeOffArmor parses result `b`, then success body `bm`:
// the vacated armor cell and one 32-byte GenericItem already addressed to MAIN.
public sealed class TakeOffArmor : IPacketSerialize
{
    public byte Result;
    public byte ArmorCell;
    public int ItemID;
    public int Count = 1;
    public byte MainCell;
    public byte State;
    public int LimitTime;
    public int TuneUpOptions;
    public int Skill0;
    public int Skill1;
    public int Durability;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)71);
        writer.Write(Result);
        if (Result != 0) return writer.Buffer;

        writer.Write(ArmorCell);
        writer.Write(ItemID);
        writer.Write(Count);
        writer.Write((byte)0); // INVEN_MAIN
        writer.Write(MainCell);
        writer.Write(State);
        writer.Write((byte)0); // x86 alignment padding
        writer.Write(LimitTime);
        writer.Write(TuneUpOptions);
        writer.Write(Skill0);
        writer.Write(Skill1);
        writer.Write(Durability);
        return writer.Buffer;
    }
}

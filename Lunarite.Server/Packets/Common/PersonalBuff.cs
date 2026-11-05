using Lunarite.Network;

namespace Lunarite.Packets.Common;

public class PersonalBuff : ISerializableType
{
    public int ID;

    public byte Group;
    public byte ModifierType;
    public byte CanBeCumulated;
    public byte Level;

    public int ExpirationDate;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(ID);

        writer.Write(Group);
        writer.Write(ModifierType);
        writer.Write(CanBeCumulated);
        writer.Write(Level);

        writer.Write(ExpirationDate);
    }
}
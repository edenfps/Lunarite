using Lunarite.Network;

namespace Lunarite.Packets.Common;

public class DurativeItem : ISerializableType
{
    public int ItemID;
    public int OptionValue;
    public int ExpirationDate;
    public int AddInfo;

    public byte CityItemGroup;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(ItemID);
        writer.Write(OptionValue);
        writer.Write(ExpirationDate);
        writer.Write(AddInfo);

        writer.Write(CityItemGroup);

        // Padding
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
    }
}
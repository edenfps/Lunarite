using Lunarite.Network;

namespace Lunarite.Packets.Common;

public class MedalInfo : ISerializableType
{
    public int MedalID;
    public int MedalCount;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(MedalID);
        writer.Write(MedalCount);
    }
}
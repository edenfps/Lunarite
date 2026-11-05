using Lunarite.Network;

namespace Lunarite.Packets.Common;

public class PlayerProperty : ISerializableType
{
    public int RespawnCount;

    public byte InvenExpandStep;
    public byte BankExpandStep;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(RespawnCount);

        writer.Write(InvenExpandStep);
        writer.Write(BankExpandStep);

        // Padding
        writer.Write((byte)0);
        writer.Write((byte)0);
    }
}
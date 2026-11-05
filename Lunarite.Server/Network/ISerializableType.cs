namespace Lunarite.Network;

public interface ISerializableType
{
    void Serialize(PacketWriter writer);
}
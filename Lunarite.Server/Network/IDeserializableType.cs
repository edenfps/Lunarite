namespace Lunarite.Network;

public interface IDeserializableType
{
    bool TryRead(ref PacketReader reader);
}
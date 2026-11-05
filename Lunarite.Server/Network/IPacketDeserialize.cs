namespace Lunarite.Network;

public interface IPacketDeserialize<T> where T : class
{
    static abstract bool TryDeserialize(ref PacketReader reader, out T? value);
}
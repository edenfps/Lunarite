using Lunarite.Network;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ServerTimeAcknowledgedHandler
{
    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // Native OnServerTime immediately sends parameterless client opcode 1.
        return reader.RemainingLength == 0;
    }
}

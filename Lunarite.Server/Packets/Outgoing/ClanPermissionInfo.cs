using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ClanPermissionInfo : IPacketSerialize
{
    public byte Result;
    public byte[] Permissions = new byte[10];
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write((byte)191); writer.Write(Result);
        if (Result == 39) foreach (byte permission in Permissions) writer.Write(permission);
        return writer.Buffer;
    }
}

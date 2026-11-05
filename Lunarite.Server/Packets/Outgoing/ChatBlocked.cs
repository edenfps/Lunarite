using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native server op198 displays HuxleyMultiHUDBase.ChatBlockMessage.
public sealed class ChatBlocked : IPacketSerialize
{
    public byte[] Serialize() => [(byte)198];
}

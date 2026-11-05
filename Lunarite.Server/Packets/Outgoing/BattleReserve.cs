using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native server op29 OnBattleReserve parses bbb: axis, reserved flag, answer.
public sealed class BattleReserve : IPacketSerialize
{
    public byte Axis;
    public byte Reserved;
    public byte Answer;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)29); writer.Write(Axis); writer.Write(Reserved); writer.Write(Answer);
        return writer.Buffer;
    }
}

using Lunarite.Network;
using Lunarite.Packets.Common;

namespace Lunarite.Packets.Outgoing;

// Native handlers sub_11113970 (op234) and sub_11113CA0 (op237).
public sealed class PersonalBuffResult : IPacketSerialize
{
    public byte Action;
    public Packets.Common.PersonalBuff? Buff;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)234);
        writer.Write(Action);
        if (Action is >= BuffResultCode.Added and <= BuffResultCode.DecrementLevel && Buff != null) Buff.Serialize(writer);
        return writer.Buffer;
    }
}

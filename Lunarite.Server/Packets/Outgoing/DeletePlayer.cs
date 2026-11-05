using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server -> client, opcode 13 (OnDeletePlayer @ 0x1110EFC0).
// Retail parser: answer "b"; on success (0), "dbb" = PID, deletion flag, remaining time.
// HuxleyMMOSelectPlayerMenu marks a timed deletion when flag==1 and removes the entry when flag==2.
public class DeletePlayer : IPacketSerialize
{
    public const byte Success = 0;
    public const byte Error = 1;
    public const byte NotFound = 2;
    public const byte AlreadyDeleted = 3;
    // DeleteMsg[5] directs a clan master to enter the city and entrust leadership first.
    public const byte ClanMasterMustEntrust = 5;
    public const byte DeleteQueued = 1;
    public const byte DeletedImmediately = 2;

    public byte Answer;
    public int Pid;
    public byte DeleteFlag = DeletedImmediately;
    public byte RemainingTime;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)13);
        writer.Write(Answer);
        if (Answer == Success)
        {
            writer.Write(Pid);
            writer.Write(DeleteFlag);
            writer.Write(RemainingTime);
        }
        return writer.Buffer;
    }
}

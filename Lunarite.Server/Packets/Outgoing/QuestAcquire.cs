using Lunarite.Network;
using Lunarite.Packets.Common;

namespace Lunarite.Packets.Outgoing;

// Native OnQuestAcquire decodes opcode 37 as `bd`.
public sealed class QuestAcquire : IPacketSerialize
{
    // Short names for the QuestResultCode answers this packet uses.
    public const byte Ok = QuestResultCode.Ok;
    public const byte Error = QuestResultCode.Error;
    public const byte NotFound = QuestResultCode.Notfound;
    public const byte NotFoundPlayer = QuestResultCode.NotfoundPlayer;
    public const byte FailLoadingRoom = QuestResultCode.FailLoadingroom;
    public const byte AlreadyAcquired = QuestResultCode.Alreadyacquire;
    public const byte Impossible = QuestResultCode.Impossible;
    public const byte NotAcquired = QuestResultCode.Notacquire;
    public const byte WrongObjective = QuestResultCode.WrongObjective;
    public const byte InventoryFull = QuestResultCode.InvenFull;
    public const byte AlreadyComplete = QuestResultCode.AlreadyComplete;
    public const byte DontSelectItem = QuestResultCode.DontSelectItem;

    public byte Answer;
    public int QuestIndex;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)37);
        writer.Write(Answer);
        writer.Write(QuestIndex);
        return writer.Buffer;
    }
}

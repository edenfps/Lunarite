using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;

namespace Lunarite.World;

public static class ChatManager
{
    public static bool IsQuestRoomNoticeAuthorized(
        int selectedPid, int roomPid, int activeQuest, int instanceToken, bool activeQuestAuthorized) =>
        selectedPid > 0 && instanceToken > 0 && roomPid == activeQuest && activeQuestAuthorized;

    internal static void SendSystemChat(Client client, string text) =>
        client.SendPacket(new Message { Kind = ServerMessageKind.ChatBox, Text = text });

    // Player-facing messages come in both languages (PlayerLanguage); GM replies stay English.
    internal static void SendSystemChat(Client client, string english, string korean) =>
        SendSystemChat(client, PlayerLanguage.Pick(client, english, korean));

    // Character index >= 10 is an Alternative (CityZoneCatalog.IsAvailableToCharacter).
    public static bool ShoutReaches(byte senderIndex, byte recipientIndex) =>
        senderIndex != 0 && recipientIndex != 0 && senderIndex >= 10 == recipientIndex >= 10;

    // ESubSystemMessage lines the client shows for op116 MESSAGE_CHATBOX_ENUM. City chat menus
    // validate nothing locally, so a failed send gets one of these instead of the echo.
    public const byte NotFoundPlayerMessage = 3;     // SubSystemMessage_NotFoundPlayer
    public const byte NotRegSquadMessage = 8;        // SubSystemMessage_NotRegSquad
    public const byte BlockPlayerMessage = 11;       // SubSystemMessage_BlockPlayer
    public const byte WhisperDiffTribeMessage = 19;  // SubSystemMessage_Whisper_DiffTribe

    // Whisper target checks in client order of severity; 0 means deliverable.
    public static byte WhisperFailure(bool targetOnline, byte senderIndex, byte targetIndex,
        bool targetBlocksSender) =>
        !targetOnline ? NotFoundPlayerMessage
        : !ShoutReaches(senderIndex, targetIndex) ? WhisperDiffTribeMessage
        : targetBlocksSender ? BlockPlayerMessage
        : (byte)0;

    public static Message BuildChatFailure(byte subSystemMessage) =>
        new() { Kind = ServerMessageKind.ChatBoxEnum, Type = subSystemMessage };

    public static byte ReceiveChatType(byte sendType) => sendType switch
    {
        ChatType.NormalSend => ChatType.NormalRecv,
        ChatType.ShoutSend => ChatType.ShoutRecv,
        ChatType.WhisperSend or ChatType.WhisperReply => ChatType.WhisperRecv,
        ChatType.SquadSend => ChatType.SquadRecv,
        ChatType.ClanSend => ChatType.ClanRecv,
        ChatType.SocialActionSend => ChatType.SocialActionRecv,
        ChatType.TeamSend => ChatType.TeamRecv,
        ChatType.TradeSend => ChatType.TradeRecv,
        _ => byte.MaxValue
    };

    public static SquadNotify BuildQuestDecisionFailureNotice(int leaderPid) => new()
    {
        Pid = leaderPid,
        Type = 0,
        Result = 0x21 // HuxleySubsystem.SQUAD_NOT_ALL_ENTER
    };
}

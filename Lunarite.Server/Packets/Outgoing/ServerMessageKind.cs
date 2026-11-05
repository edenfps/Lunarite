namespace Lunarite.Packets.Outgoing;

// op116 message kinds (HuxleySubsystem.OnMessage).
public static class ServerMessageKind
{
    public const byte ChatBox = 0;      // MESSAGE_CHATBOX: text in the chat box
    public const byte DialogBox = 1;    // MESSAGE_DIALOGBOX: localized message box by enum
    public const byte Notice = 2;       // MESSAGE_NOTICE: banner
    public const byte ChatBoxEnum = 3;  // MESSAGE_CHATBOX_ENUM: localized chat-box line by enum
    public const byte ClanNotice = 4;   // clan MESSAGE_NOTICE
}

using System;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Notice colors are plain RGB bytes.
public sealed class Message : IPacketSerialize
{
    public byte Kind;
    public byte Type;
    public string Text = "";
    public byte LifeSeconds = 5;
    public byte Red;
    public byte Green;
    public byte Blue = 255;

    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)116); w.Write(Kind);
        switch (Kind)
        {
            case ServerMessageKind.ChatBox: w.Write(Text); break;
            case ServerMessageKind.DialogBox or ServerMessageKind.ChatBoxEnum: w.Write(Type); break;
            case ServerMessageKind.Notice or ServerMessageKind.ClanNotice:
                w.Write(Text); w.Write(LifeSeconds); w.Write(Red); w.Write(Green); w.Write(Blue);
                break;
            default: throw new InvalidOperationException($"Unknown server-message kind {Kind}.");
        }
        return w.Buffer;
    }
}

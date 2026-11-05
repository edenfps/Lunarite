using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server op16 OnParameter "bd" (param, value) -> HuxleySubsystem.OnParameter -> m_SelectPlayer.
// ENUM_PARAMETER (HuxleyPlayerWrapper): 0=PID 1=LEVEL 2=MONEY 3=BP 4=LP 5=LUNARITE 6=STYLE
// 7=RANK 8=JOB ... The style-select menu only sends SendParameter(STYLE); byStyle is set when
// the server echoes op16.
public class Parameter : IPacketSerialize
{
    public const byte PARAMETER_LEVEL = 1;
    public const byte PARAMETER_STYLE = 6;
    public const byte PARAMETER_RANK = 7;
    public const byte PARAMETER_MONEY = 2;
    public const byte PARAMETER_BP = 3;
    public const byte PARAMETER_LP = 4;
    public const byte PARAMETER_TOTALKILL = 13;  // m_nTotalKill, shown as Kill/Death on the ID panel
    public const byte PARAMETER_TOTALDEATH = 14; // m_nTotalDeath
    public const byte PARAMETER_QT = 15;
    public const byte PARAMETER_RESPAWNCOUNT = 16;
    public const byte PARAMETER_PCROOM = 17; // m_stPlayer.byPCRoomValid

    public byte Param;
    public int Value;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)16);
        writer.Write(Param);
        writer.Write(Value);
        return writer.Buffer;
    }
}

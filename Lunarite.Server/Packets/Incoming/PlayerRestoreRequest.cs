using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// Client -> server, opcode 13. UHuxleySubsystem.SendPlayerRestore(int PID).
public class PlayerRestoreRequest : IPacketDeserialize<PlayerRestoreRequest>
{
    public int Pid;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out PlayerRestoreRequest? packet)
    {
        packet = new PlayerRestoreRequest();
        return reader.TryRead(out packet.Pid) && reader.RemainingLength == 0;
    }

    public override string ToString() => $"PID: {Pid}";
}

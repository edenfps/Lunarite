using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// Client -> server, opcode 12. UHuxleySubsystem.SendPlayerDelete(int PID, string SSN).
// The retail client writes "ds": character PID followed by a null-terminated SSN string.
public class PlayerDeleteRequest : IPacketDeserialize<PlayerDeleteRequest>
{
    public int Pid;
    public string? SSN;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out PlayerDeleteRequest? packet)
    {
        packet = new PlayerDeleteRequest();
        return reader.TryRead(out packet.Pid)
            && reader.TryRead(out packet.SSN)
            && reader.RemainingLength == 0;
    }

    public override string ToString() => $"PID: {Pid}";
}

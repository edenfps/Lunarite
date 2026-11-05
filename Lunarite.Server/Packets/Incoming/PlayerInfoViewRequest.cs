using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// Client op15 has two native senders: RequestAnotherPlayerInfo writes `db` (PID plus the
// Simulation-player flag) and _SendPlayerInfoView (sub_111C18E0) writes plain `d` (PID only).
// Server op17 returns the compact profile and exact 32-byte AnotherBattleRecord.
public readonly record struct PlayerInfoViewRequest(int Pid)
{
    public static bool TryDeserialize(ref PacketReader reader, out PlayerInfoViewRequest packet)
    {
        packet = default;
        if (!reader.TryRead(out int pid)) return false;
        if (reader.RemainingLength != 0 && (!reader.TryRead(out byte _) || reader.RemainingLength != 0))
            return false;
        packet = new PlayerInfoViewRequest(pid);
        return true;
    }
}

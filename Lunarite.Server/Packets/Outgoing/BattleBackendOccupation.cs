using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// CServerTcpLink incoming opcode 11 (ProcOccupation, 0x111AB930), native format b: the species
// holding the battleground (ESpeciesType 0 none, 1 Sapiens, 2 Alternative). It reaches
// HuxleyBattleGame.SetOccupationSpecies, and HuxleyMultiTeamGame awards a timed-out draw to that
// species.
public sealed class BattleBackendOccupation : IPacketSerialize
{
    public byte Species;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)11);
        writer.Write(Species);
        return writer.Buffer;
    }
}

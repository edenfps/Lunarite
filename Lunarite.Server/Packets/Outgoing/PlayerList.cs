using System;
using System.Collections.Generic;

using Lunarite.Network;
using Lunarite.Packets.Common;

namespace Lunarite.Packets.Outgoing;

// Server op10 OnPlayerList (0x11134240): the select-screen roster.
//   header   "bbbdb" = four PC-room fields (-> OnPCRoomInfo), then a byte count.
//   per-char "dsbwbbbmm" = PID, Name, Index/species, Level, Style, Rank, deletion minutes,
//                          Custom appearance m[25], armor preview m[160] (5x32).
public class PlayerList : IPacketSerialize
{
    public sealed class Entry
    {
        public int Pid;
        public string Name = "";
        public byte Index;      // species TypeIndex (drives the select-screen model)
        public short Level = 1;
        public byte Style;
        public byte Rank;
        public byte DeleteMinutesRemaining;
        public Custom Custom = new();
        public byte[] ArmorPreview = new byte[160];
    }

    public List<Entry> Characters = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)10);

        // PC-room header (OnPCRoomInfo); zero means no premium.
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(0);

        writer.Write((byte)Characters.Count);

        foreach (var character in Characters)
        {
            writer.Write(character.Pid);
            writer.Write(character.Name);
            writer.Write(character.Index);
            writer.Write(character.Level);
            writer.Write(character.Style);
            writer.Write(character.Rank);
            // The seventh per-character field becomes F_Player_.fDelRemainTime. The client
            // counts it down as minutes and exposes its Restore button while it is non-zero.
            writer.Write(character.DeleteMinutesRemaining);
            character.Custom.Serialize(writer);      // m[25] appearance
            if (character.ArmorPreview.Length != 160)
                throw new InvalidOperationException("player-list armor preview must be five 32-byte GenericItems");
            writer.Write(character.ArmorPreview);    // m[160] armor preview (5x32)
        }

        return writer.Buffer;
    }
}

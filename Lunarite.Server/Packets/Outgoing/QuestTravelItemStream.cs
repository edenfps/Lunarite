using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Network;
using Lunarite.Packets.Common;

namespace Lunarite.Packets.Outgoing;

// Travel inventory body shared by op42 and op55, which install the same loadout.
internal static class QuestTravelItemStream
{
    public static void Write(PacketWriter writer, IReadOnlyList<QuestCreate.Item> items,
        bool displacedP2P = false)
    {
        var counts = new byte[7];
        foreach (var item in items)
        {
            if (item.InvenIndex >= counts.Length) continue;
            if (counts[item.InvenIndex] == byte.MaxValue)
                throw new InvalidOperationException("Quest travel inventory count exceeds byte capacity.");
            counts[item.InvenIndex]++;
        }
        // Type-4 OnQuestCreate omits the four-byte CQuestGamePlayerBackupInfo serializer,
        // although HuxleyQuestGame still advances over its item-count dword. One terminal
        // record supplies the last real item's nGeneral3 across that 32-byte boundary. Its
        // displaced ItemID is zero, so AddItemByteStream ignores it.
        if (displacedP2P && items.Count > 0)
            counts[6]++;
        writer.Write(counts);

        foreach (var item in items)
        {
            if (item.InvenIndex >= counts.Length) continue;
            if (!displacedP2P)
            {
                WriteGenericItem(writer, item);
                continue;
            }

            // Type 4: native storage reads the record at +0, the cooked quest script at +4.
            // ByteStream2GenericItem is a raw 32-byte memcpy (sub_111D92B0).
            // The stream's first dword is the quest-item count ParsePlayerBackupData consumes, and
            // BELT cell 0 is the first slot AddDatas emits. An item ID there runs the GetQuestItem loop
            // past every item and the host loses all gear; -1 is occupied for sub_11116470, no items
            // for the script.
            writer.Write(item.InvenIndex == InventoryType.Belt && item.Cell == 0 ? -1 : item.ItemID); // +0  storage occupancy
            writer.Write(item.ItemID);          // +4  displaced arData nItemID
            writer.Write(item.InvenIndex);      // +8  storage inventory
            writer.Write(item.Cell);            // +9  storage cell
            writer.Write((byte)0);              // +10
            writer.Write((byte)0);              // +11
            writer.Write(item.InvenIndex);      // +12 displaced arData inventory
            writer.Write(item.Cell);            // +13 displaced arData cell
            writer.Write(item.State);           // +14 displaced arData state
            writer.Write((byte)0);              // +15
            writer.Write(item.LimitTime);       // +16 -> displaced LimitTime
            writer.Write(item.TuneUpOptions);   // +20 -> displaced TuneUpOptions
            writer.Write(item.Skill0);          // +24 -> displaced nGeneral1/socket skill
            writer.Write(item.Skill1);          // +28 -> displaced nGeneral2
        }

        if (displacedP2P && items.Count > 0)
        {
            // CMMOVerify::AddDatas walks fixed inventories/cells in order, so the bridged value
            // belongs to the last occupied fixed slot (boots normally), not the list's last item.
            var last = items
                .Where(item => item.InvenIndex < counts.Length)
                .OrderBy(item => item.InvenIndex)
                .ThenBy(item => item.Cell)
                .Last();
            writer.Write(last.Durability);       // +0 -> prior record's displaced nGeneral3
            writer.Write(0);                     // +4 -> sentinel displaced ItemID (ignored)
            // Armor has five slots (0..4), so the record after boots is skill inventory cell 0.
            // An armor cell 5 record is discarded, which leaves boots at zero durability and
            // makes InitPlayerSocketSkills skip the socket.
            writer.Write((byte)6);               // +8 storage inventory (INVEN_SKILL)
            writer.Write((byte)0);               // +9 storage cell
            writer.Write((short)0);              // +10
            writer.Write(new byte[20]);          // +12..31
        }
    }

    private static void WriteGenericItem(PacketWriter writer, QuestCreate.Item item)
    {
        writer.Write(item.ItemID);
        writer.Write(item.Count);
        writer.Write(item.InvenIndex);
        writer.Write(item.Cell);
        writer.Write(item.State);
        writer.Write((byte)0);
        writer.Write(item.LimitTime);
        writer.Write(item.TuneUpOptions);
        writer.Write(item.Skill0);
        writer.Write(item.Skill1);
        writer.Write(item.Durability);
    }
}

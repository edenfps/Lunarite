using Lunarite.Network;
using Lunarite.Storage;

namespace Lunarite.Packets.Outgoing;

internal static class SkillResultWriter
{
    public static void WriteItem(PacketWriter w, PlayerStore.GenericItem? item)
    {
        w.Write(item?.ItemId ?? 0); w.Write(item?.Count ?? 0);
        w.Write(item?.InventoryIndex ?? (byte)0); w.Write(item?.InventoryCell ?? (byte)0);
        w.Write(item?.State ?? (byte)0); w.Write((byte)0);
        w.Write(item?.LimitTime ?? 0); w.Write(item?.TuneUpOptions ?? 0);
        w.Write(item?.Skill ?? 0); w.Write(item?.Skill1 ?? 0); w.Write(item?.Durability ?? 0);
    }
}

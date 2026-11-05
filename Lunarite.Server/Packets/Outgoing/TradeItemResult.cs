namespace Lunarite.Packets.Outgoing;

// Result byte of AddItem/SubItem: Self confirms the sender's own slot, Partner shows the other
// side's slot, Refused re-syncs the sender (OnTradeAddSubItemSelf).
public static class TradeItemResult
{
    public const byte Self = 0;       // OnTradeAddItemSelf / OnTradeSubItemSelf
    public const byte Partner = 1;    // OnTradeAddItem(slot, item) / OnTradeSubItem(slot)
    public const byte Refused = 2;    // OnTradeAddSubItemSelf
    public const byte Blocked = 3;    // OnTradeAddItemBlocked
    public const byte Restricted = 4; // OnTradeAddItemRestricted
}

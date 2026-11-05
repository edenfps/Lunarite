using Lunarite.Storage;

namespace Lunarite.World;

// An item sold to a vendor that op177 can still buy back (UIMMOTradeMenu undo list).
public sealed record UndoItem(byte Serial, PlayerStore.GenericItem Item, long Price);

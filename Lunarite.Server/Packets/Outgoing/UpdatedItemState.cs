using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native op214 / ProcUpdateItemState: inventory, cell, new GenericItem.byState.
public sealed class UpdatedItemState : IPacketSerialize
{
    public byte Inventory;
    public byte Cell;
    public byte State;
    public byte[] Serialize() => [(byte)214, Inventory, Cell, State];
}

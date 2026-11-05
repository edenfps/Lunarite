using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

/// <summary>
/// Requests a UI-wide refresh after an inventory transaction.
/// Native op90 dispatches 0 to OnUpdateInventoryViews and 1 to OnUpdateEquipViews.
/// </summary>
public sealed class UpdateInventoryViews : IPacketSerialize
{
    public const byte Inventory = 0;
    public const byte Equipment = 1;

    public byte View;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)90);
        writer.Write(View);
        return writer.Buffer;
    }
}

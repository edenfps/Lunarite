namespace Lunarite.Packets.Outgoing;

public readonly record struct ItemStateEntry(byte Inventory, byte Cell, byte State);

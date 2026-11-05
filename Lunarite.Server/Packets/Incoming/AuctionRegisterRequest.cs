using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// HuxleySubsystem.SendAuctionRegister(byte byMainCell, int nSellPrice, byte byBidPeriod).
// The native sender (sub_111C4BB0) reorders the arguments on the wire:
// Send(146, "bbd", cell, period, price).
public readonly record struct AuctionRegisterRequest(byte MainCell, int SellPrice, byte BidPeriod)
{
    public static bool TryDeserialize(ref PacketReader reader, out AuctionRegisterRequest packet)
    {
        packet = default;
        if (!reader.TryRead(out byte mainCell)
            || !reader.TryRead(out byte bidPeriod)
            || !reader.TryRead(out int sellPrice)
            || reader.RemainingLength != 0)
            return false;

        packet = new AuctionRegisterRequest(mainCell, sellPrice, bidPeriod);
        return true;
    }
}

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// HuxleySubsystem.SendAuctionList(byte category, int page, bool usable, byte grade, string search).
// The script signature takes an int page, but the native sender (sub_111C73E0) writes
// Send(143, "bbbbs", ...), so the page (a PAGE_* direction) is a single byte on the wire.
public readonly record struct AuctionListRequest(byte Category, byte Page, bool Usable, byte Grade, string Search)
{
    public static bool TryDeserialize(ref PacketReader reader, out AuctionListRequest packet)
    {
        packet = default;
        if (!reader.TryRead(out byte category)
            || !reader.TryRead(out byte page)
            || !reader.TryRead(out bool usable)
            || !reader.TryRead(out byte grade)
            || !reader.TryRead(out string? search)
            || reader.RemainingLength != 0)
            return false;

        packet = new AuctionListRequest(category, page, usable, grade, search ?? "");
        return true;
    }
}

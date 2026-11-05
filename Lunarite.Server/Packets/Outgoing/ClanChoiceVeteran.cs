using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native ProcClanChoiceVeteran (server op194) consumes the localized message
// index directly: 46 announces veteran election; every other value selects the
// veteran-removal message at 47. This is independent of officer appointment grades.
public sealed class ClanChoiceVeteran : IPacketSerialize
{
    public byte MessageIndex;
    public byte[] Serialize() => [(byte)194, MessageIndex];
}

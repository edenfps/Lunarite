using System.Collections.Generic;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed class ClanManager
{
    private readonly CityManager _cityManager;

    private readonly CharacterStore _characterStore;
    private readonly ClanStore _clanStore;

    public ClanManager(CharacterStore characterStore, ClanStore clanStore, CityManager cityManager)
    {
        _characterStore = characterStore;
        _clanStore = clanStore;
        _cityManager = cityManager;
    }

    // HuxleyClanMemberMenu.MemberMapStateString: 0 Off, 1 Connected, 2 City, 3 Battled,
    // 4 Quest, 5 Virtual Combat (Simulation Center match).
    public static int ClanMapState(bool online, bool inQuest, bool inBattle = false, bool inSimulation = false) =>
        !online ? 0 : inSimulation ? 5 : inBattle ? 3 : inQuest ? 4 : 2;

    // Retail TypeIndex values 1/6 are Sapiens and 11/16 are Alternatives.
    // CLAN_INVALIDTRIBE (52) rejects cross-faction clan membership.
    public static bool CanShareClan(byte firstCharacterIndex, byte secondCharacterIndex) =>
        (firstCharacterIndex < 11) == (secondCharacterIndex < 11);

    // The client learns its own clan grade and mark only from op42 at login or op185, so push
    // op185 after every membership, grade or mark change.
    internal void PushClanMyInfo(int pid)
    {
        if (!Game.TryGetClient(pid, out var target)) return;
        var clan = _clanStore.ForPid(pid);
        target.SendPacket(new ClanMyInfo { ClanName = clan?.Name ?? "",
            Grade = _clanStore.Grade(pid), Mark = clan?.Mark ?? 0 });
    }

    internal static void SendToOnlineClan(IEnumerable<int> memberPids, IPacketSerialize packet, int exceptPid = 0)
    {
        foreach (int memberPid in memberPids)
            if (memberPid != exceptPid && Game.TryGetClient(memberPid, out var memberClient))
                memberClient.SendPacket(packet);
    }

    // Native op192: "<name> has come online", sent to the clan's other online members at login.
    internal void AnnounceClanLogin(int pid)
    {
        if (_clanStore.ForPid(pid) == null) return;
        SendToOnlineClan(_clanStore.MemberPids(pid),
            new ClanEntry { MemberName = _characterStore.Get(pid)?.Name ?? "" }, pid);
    }

    internal void BroadcastClanHeadline(int pid)
    {
        if (!Game.TryGetClient(pid, out var selected)) return;
        var packet = BuildClanHeadline(pid);
        selected.SendPacket(packet);
        _cityManager.BroadcastPresence(selected, packet);
    }

    public UpdateClanHeadLine BuildClanHeadline(int pid)
    {
        var clan = _clanStore.ForPid(pid);
        return new UpdateClanHeadLine
            { Pid = pid, ClanName = clan?.Name ?? "", Rank = _clanStore.Grade(pid) };
    }
}

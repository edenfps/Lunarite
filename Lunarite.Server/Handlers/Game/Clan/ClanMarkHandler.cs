using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ClanMarkHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static CharacterManager _characterManager = null!;
    private static ClanManager _clanManager = null!;
    private static ClanStore _clanStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _clanStore = serviceProvider.GetRequiredService<ClanStore>();
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _clanManager = serviceProvider.GetRequiredService<ClanManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int mark) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        byte result = 0;
        _multiStoreTransaction.Run(() => result = _clanStore.SetMark(pid, mark, ClanStore.MarkChangeCost));
        client.SendPacket(new ClanActionResult { Opcode = 187, Result = result });
        if (result == 30)
        {
            _characterManager.SendMoneyParameter(client, pid);
            foreach (int memberPid in _clanStore.MemberPids(pid)) _clanManager.PushClanMyInfo(memberPid);
        }
        return true;
    }
}

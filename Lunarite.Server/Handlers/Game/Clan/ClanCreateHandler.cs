using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ClanCreateHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ClanManager _clanManager = null!;
    private static ClanStore _clanStore = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _clanStore = serviceProvider.GetRequiredService<ClanStore>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _clanManager = serviceProvider.GetRequiredService<ClanManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out string? name) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        byte result = 1;
        _multiStoreTransaction.Run(() => result = _clanStore.Create(pid, name ?? "", ClanStore.CreationCost));
        client.SendPacket(new ClanActionResult { Opcode = 176, Result = result });
        if (result == 0)
        {
            client.SendPacket(new Parameter
            {
                Param = Parameter.PARAMETER_MONEY,
                Value = (int)Math.Min(int.MaxValue, _playerStore.GetProgress(pid).Money)
            });
            _clanManager.BroadcastClanHeadline(pid);
            _clanManager.PushClanMyInfo(pid);
        }
        return true;
    }
}

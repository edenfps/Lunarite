using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class UpdateSkillSlotHandler
{
    private static CityManager _cityManager = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte slot1) || !reader.TryRead(out byte slot2)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        bool swapped = _playerStore.SwapSkillSlots(pid, slot1, slot2);
        client.SendPacket(new UpdateSkillSlot { Result = swapped ? (byte)0 : (byte)1, Slot1 = slot1, Slot2 = slot2 });
        if (swapped)
            _cityManager.BroadcastPresence(client, new ListenUpdateSkillSlot { Pid = pid, SkillSlots = _playerStore.GetSkillSlots(pid) });
        return true;
    }
}

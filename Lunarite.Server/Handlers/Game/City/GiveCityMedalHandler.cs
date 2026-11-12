using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class GiveCityMedalHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static CharacterManager _characterManager = null!;
    private static ItemManager _itemManager = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        int pid = Game.PidOf(client);
        if (!reader.TryRead(out int aId) || !reader.TryRead(out int aCount)
            || !reader.TryRead(out int bId) || !reader.TryRead(out int bCount)
            || reader.RemainingLength != 0 || pid <= 0)
            return false;
        var before = _playerStore.GetInventory(pid, () => [])
            .Where(x => x.InventoryIndex == InventoryType.Main).ToDictionary(x => x.InventoryCell);
        bool donated = false, promoted = false;
        byte oldRank = 0, newRank = 0;
        int rankLp = 0;
        _multiStoreTransaction.Run(() =>
        {
            donated = _playerStore.TryDonateCityMedals(pid, aId, aCount, bId, bCount);
            if (donated) _itemManager.SynchronizeQuestPickupObjectives(pid, [aId, bId]);
            if (donated) promoted = _playerStore.TryPromoteRank(pid, out oldRank, out newRank, out rankLp);
        });
        if (!donated)
        {
            client.SendPacket(new MedalParameterUpdate { Success = false });
            return true;
        }

        var after = _playerStore.GetInventory(pid, () => [])
            .Where(x => x.InventoryIndex == InventoryType.Main).ToDictionary(x => x.InventoryCell);
        foreach (byte cell in before.Keys.Where(cell => !after.ContainsKey(cell)))
            client.SendPacket(new RemoveItem { InventoryIndex = 0, Cell = cell });
        // A medal stack that only got smaller takes UpdateItemNum: the client refuses op82 InsertItem on an occupied
        // cell, so it kept showing the old count and the next larger turn-in asked for medals that were gone.
        foreach (var item in after.Values.Where(item => before.TryGetValue(item.InventoryCell, out var old)
                     && old.ItemId == item.ItemId && old.Count != item.Count))
            client.SendPacket(new UpdateItemNum { Inventory = InventoryType.Main, Cell = item.InventoryCell, Count = item.Count });
        foreach (var item in after.Values.Where(item => !before.TryGetValue(item.InventoryCell, out var old)
                     || old.ItemId != item.ItemId))
            client.SendPacket(new InsertItem
            {
                ItemID = item.ItemId, Count = item.Count, InventoryIndex = item.InventoryIndex, Cell = item.InventoryCell,
                State = item.State, LimitTime = item.LimitTime, TuneUpOptions = item.TuneUpOptions,
                Skill0 = item.Skill, Skill1 = item.Skill1, Durability = item.Durability
            });

        var progress = _playerStore.GetProgress(pid);
        client.SendPacket(new MedalParameterUpdate { Success = true, MedalId = aId, Count = progress.AMedalsDonated });
        client.SendPacket(new MedalParameterUpdate { Success = true, MedalId = bId, Count = progress.BMedalsDonated });
        if (promoted) _characterManager.PublishRankUp(client, pid, oldRank, newRank, rankLp);
        return true;
    }
}

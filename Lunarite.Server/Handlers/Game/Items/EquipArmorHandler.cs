using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

// Native client op67 `bb`: move MAIN[mainCell] into ARMOR[armorCell]. Server op68 echoes
// result and cells; OnEquipArmor commits the same transaction client-side.
[PacketHandler]
public static class EquipArmorHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ItemCatalog _itemCatalog = null!;
    private static CharacterManager _characterManager = null!;
    private static CityManager _cityManager = null!;
    private static ItemManager _itemManager = null!;
    private static ILogger _logger = null!;
    private static EquipmentRules _equipmentRules = null!;
    private static PlayerStore _playerStore = null!;
    private static WardrobeManager _wardrobeManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(EquipArmorHandler));

        _equipmentRules = serviceProvider.GetRequiredService<EquipmentRules>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _itemCatalog = serviceProvider.GetRequiredService<ItemCatalog>();
        _wardrobeManager = serviceProvider.GetRequiredService<WardrobeManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte armorCell) || !reader.TryRead(out byte mainCell)
            || reader.RemainingLength != 0)
        {
            _logger.LogWarning("Failed to read EquipArmor.");
            return false;
        }

        int pid = Game.PidOf(client);
        var source = _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(x => x.InventoryIndex == InventoryType.Main && x.InventoryCell == mainCell);
        int replacedItemId = _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(x => x.InventoryIndex == InventoryType.Armor && x.InventoryCell == armorCell)?.ItemId ?? 0;
        var data = source == null ? null : _itemCatalog.Get(source.ItemId);
        byte result = _equipmentRules.ValidateArmor(pid, armorCell, mainCell, source, data);
        List<PlayerStore.SocketMove> socketMoves = [];
        if (result == 0)
            _multiStoreTransaction.Run(() =>
            {
                result = _playerStore.SwapSlots(pid, InventoryType.Armor, armorCell, InventoryType.Main, mainCell)
                    ? (byte)0 : (byte)1;
                if (result == 0) _itemManager.SynchronizeQuestPickupObjectives(pid, [source!.ItemId, replacedItemId]);
                // Socket skills stay with the player: into the new piece, or back to the bag (PlayerStore.Sockets).
                if (result == 0 && replacedItemId != 0) socketMoves = _playerStore.ReleaseSockets(pid, mainCell, armorCell);
            });
        bool moved = result == 0;
        client.SendPacket(new EquipArmor
        {
            Result = result,
            ArmorCell = armorCell,
            MainCell = mainCell
        });
        if (moved && socketMoves.Count > 0) SendSocketMoves(client, pid, socketMoves, armorCell, mainCell);
        if (moved)
        {
            if (_playerStore.TryBindEquipped(pid, InventoryType.Armor, armorCell, out byte armorState))
                client.SendPacket(new UpdatedItemState { Inventory = InventoryType.Armor, Cell = armorCell, State = armorState });
            if (_playerStore.TryStartEquippedLimit(pid, InventoryType.Armor, armorCell, out int armorLimit))
                client.SendPacket(new ItemLimitTimeUpdate
                {
                    Inventory = InventoryType.Armor, Cell = armorCell, LimitTime = armorLimit
                });
            int itemId = _playerStore.GetInventory(pid, () => [])
                .FirstOrDefault(x => x.InventoryIndex == InventoryType.Armor && x.InventoryCell == armorCell)?.ItemId ?? 0;
            _cityManager.BroadcastPresence(client, new EquipArmorOther { Pid = pid, Part = armorCell, ItemId = _wardrobeManager.Shown(pid, armorCell, itemId) });
            var equippedArmor = _characterManager.BuildOtherPlayerArmorSnapshot(pid)
                .FirstOrDefault(update => update.Part == armorCell);
            if (equippedArmor != null)
                _cityManager.BroadcastPresence(client, equippedArmor);
        }
        _logger.LogInformation("[Items] PID {0} armor equip MAIN[{1}] -> ARMOR[{2}], result={3}, sockets moved {4}.",
            pid, mainCell, armorCell, result, socketMoves.Count);
        return true;
    }

    // The client swapped its own copies on op67, so the records that changed are sent again: the new piece (skills
    // moved in), the old piece now in MAIN (skills gone) and each skill that came back to the bag. op82 refuses an
    // occupied cell, so a changed cell is emptied first (op81).
    internal static void SendSocketMoves(Client client, int pid, List<PlayerStore.SocketMove> moves, byte? armorCell, byte? oldMainCell)
    {
        var inventory = _playerStore.GetInventory(pid, () => []);
        void Refresh(byte inven, byte cell)
        {
            var item = inventory.FirstOrDefault(i => i.InventoryIndex == inven && i.InventoryCell == cell);
            if (item == null) return;
            client.SendPacket(new RemoveItem { InventoryIndex = inven, Cell = cell });
            ItemManager.SendInventoryItem(client, item);
        }
        if (armorCell is byte armor && moves.Any(m => m.Transferred)) Refresh(InventoryType.Armor, armor);
        if (oldMainCell is byte main) Refresh(InventoryType.Main, main);
        foreach (var move in moves.Where(m => !m.Transferred))
        {
            if (move.FromSkillCell is byte skillCell)
                client.SendPacket(new RemoveItem { InventoryIndex = InventoryType.Skill, Cell = skillCell });
            var returned = move.ToMainCell is byte to
                ? inventory.FirstOrDefault(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell == to) : null;
            if (returned != null) ItemManager.SendInventoryItem(client, returned);
        }
        // The client takes the records but its open Equipment/Inventory windows keep the old picture until reopened;
        // the LunariteMenus Wardrobe channel redraws them (hidden: stock chat drops lines starting with '/').
        client.SendPacket(new Chat { Type = ChatType.NormalRecv, From = "", To = "", Message = "/wardrobe refresh" });
    }
}

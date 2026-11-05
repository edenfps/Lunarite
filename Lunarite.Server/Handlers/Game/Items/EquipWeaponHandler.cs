using System;
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

// Op68 EquipWeapon: MAIN[MainCell] -> WEAPON[WeaponCell] under ITEM_TRANSACTION_EQUIPWEAPON.
// A result-0 reply fires OnEquipWeapon, which commits the move and ends the transaction.
[PacketHandler]
public static class EquipWeaponHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ItemCatalog _itemCatalog = null!;
    private static ItemManager _itemManager = null!;
    private static ILogger _logger = null!;
    private static EquipmentRules _equipmentRules = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(EquipWeaponHandler));

        _equipmentRules = serviceProvider.GetRequiredService<EquipmentRules>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _itemCatalog = serviceProvider.GetRequiredService<ItemCatalog>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte weaponCell) || !reader.TryRead(out byte mainCell)
            || reader.RemainingLength != 0)
        {
            _logger.LogWarning("Failed to read {0}.", "EquipWeapon");
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( WeaponCell={1} MainCell={2} )", "EquipWeapon", weaponCell, mainCell);

        int pid = Game.PidOf(client);
        byte result;
        var source = _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(x => x.InventoryIndex == InventoryType.Main && x.InventoryCell == mainCell);
        int replacedItemId = _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(x => x.InventoryIndex == InventoryType.Weapon && x.InventoryCell == weaponCell)?.ItemId ?? 0;
        var data = source == null ? null : _itemCatalog.Get(source.ItemId);
        result = _equipmentRules.ValidateWeapon(pid, weaponCell, mainCell, source, data);
        if (result == 0)
            _multiStoreTransaction.Run(() =>
            {
                result = _playerStore.SwapSlots(pid, InventoryType.Weapon, weaponCell, InventoryType.Main, mainCell)
                    ? (byte)0 : (byte)1;
                if (result == 0) _itemManager.SynchronizeQuestPickupObjectives(pid, [source!.ItemId, replacedItemId]);
            });

        bool moved = result == 0;
        client.SendPacket(new EquipWeapon { Result = result, WeaponCell = weaponCell, MainCell = mainCell });
        if (moved && _playerStore.TryBindEquipped(pid, InventoryType.Weapon, weaponCell, out byte weaponState))
            client.SendPacket(new UpdatedItemState { Inventory = InventoryType.Weapon, Cell = weaponCell, State = weaponState });
        if (moved && _playerStore.TryStartEquippedLimit(Game.PidOf(client), InventoryType.Weapon, weaponCell, out int weaponLimit))
            client.SendPacket(new ItemLimitTimeUpdate
            {
                Inventory = InventoryType.Weapon, Cell = weaponCell, LimitTime = weaponLimit
            });
        _logger.LogInformation("[Items] PID {0} weapon equip MAIN[{1}] -> WEAPON[{2}], result={3}.",
            pid, mainCell, weaponCell, result);
        return true;
    }
}

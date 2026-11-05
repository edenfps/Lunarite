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
public static class ResetLicenseHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ItemManager _itemManager = null!;
    private static LicenseStore _licenseStore = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _licenseStore = serviceProvider.GetRequiredService<LicenseStore>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte mainCell) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        byte result = LicenseResetResultCode.Internalerror;
        long lp = _playerStore.GetProgress(pid).LP;
        int[] licenses = _licenseStore.Effective(pid);
        var resetItem = _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell == mainCell);
        if (resetItem?.ItemId != 670001) result = LicenseResetResultCode.Notresetitem;
        else if (_playerStore.GetInventory(pid, () => []).Any(i => i.InventoryIndex == InventoryType.Weapon))
            result = LicenseResetResultCode.Mustunequipweapons;
        // ECIG_TermLicense rows are entitlements, not LP purchases, and cannot be refunded.
        // With no permanent license the reset item is not consumed and retail NoLicense is returned.
        else if (!_licenseStore.HasPermanentLicenses(pid)) result = LicenseResetResultCode.Nolicense;
        else
        {
            _multiStoreTransaction.Run(() =>
            {
                if (!_playerStore.TryConsumeMainItem(pid, mainCell, 670001, out _))
                {
                    result = LicenseResetResultCode.Internalerror;
                    return;
                }
                int refund = _licenseStore.ClearAndRefund(pid);
                lp = _playerStore.AddLicensePoints(pid, refund);
                licenses = _licenseStore.Effective(pid);
                _itemManager.SynchronizeQuestPickupObjective(pid, 670001);
                result = LicenseResetResultCode.Ok;
            });
            if (result == 0)
            {
                client.SendPacket(new RemoveItem { InventoryIndex = InventoryType.Main, Cell = mainCell });
                var remaining = _playerStore.GetInventory(pid, () => [])
                    .FirstOrDefault(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell == mainCell);
                if (remaining != null) ItemManager.SendInventoryItem(client, remaining);
            }
        }
        client.SendPacket(new ResetLicense
        {
            Result = result, LicensePoints = (int)Math.Min(int.MaxValue, lp), Licenses = licenses
        });
        return true;
    }
}

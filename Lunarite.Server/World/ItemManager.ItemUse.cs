using System;
using System.Linq;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed partial class ItemManager
{
    internal readonly record struct UseItemOutcome(byte Result, byte UseType, byte[]? ItemData = null);

    // The used item's cell after consumption: removed, then re-sent if a stack remains.
    private void RefreshMainCell(Client client, int pid, byte mainCell)
    {
        client.SendPacket(new RemoveItem { InventoryIndex = InventoryType.Main, Cell = mainCell });
        var remaining = _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell == mainCell);
        if (remaining != null) SendInventoryItem(client, remaining);
    }

    internal UseItemOutcome UseExpansionItem(Client client, int pid, byte mainCell,
        PlayerStore.GenericItem item, ItemCatalog.Item data, bool bank)
    {
        byte useType = bank ? UseItemResultCode.Bankexpand : UseItemResultCode.Invenexpand;
        bool used = false;
        _multiStoreTransaction.Run(() =>
        {
            used = _playerStore.TryUseExpansionItem(pid, mainCell, bank, data.GeneralValue, out _);
            if (used) SynchronizeQuestPickupObjective(pid, item.ItemId);
        });
        if (!used) return new(ItemResultCode.NoMoreExpand, useType);
        RefreshMainCell(client, pid, mainCell);
        return new(ItemResultCode.Ok, useType);
    }

    // The spray recolors the equipped upbody; everyone who can see the player gets the new armor.
    internal UseItemOutcome UseColorSpray(Client client, int pid, byte mainCell, PlayerStore.GenericItem item)
    {
        if (!_playerStore.GetInventory(pid, () => []).Any(i => i.InventoryIndex == InventoryType.Armor && i.InventoryCell == 1))
            return new(ItemResultCode.NoUpbody, UseItemResultCode.Spray);
        PlayerStore.GenericItem? upbody = null;
        PlayerStore.GenericItem? remaining = null;
        bool used = false;
        _multiStoreTransaction.Run(() =>
        {
            used = _playerStore.TryUseColorSpray(pid, mainCell, item.ItemId, out remaining, out upbody);
            if (used) SynchronizeQuestPickupObjective(pid, item.ItemId);
        });
        if (!used) return new(ItemResultCode.Error, UseItemResultCode.Spray);
        client.SendPacket(new RemoveItem { InventoryIndex = InventoryType.Main, Cell = mainCell });
        if (remaining != null) SendInventoryItem(client, remaining);
        if (upbody == null) return new(ItemResultCode.Ok, UseItemResultCode.Spray);
        _cityManager.BroadcastPresence(client, new UpdateArmorOther { Pid = pid, Part = 1, Item = upbody });
        _cityManager.BroadcastPresence(client, new QuestUpdateItem { Pid = pid, Item = upbody });
        return new(ItemResultCode.Ok, UseItemResultCode.Spray, SerializeGenericItem(upbody));
    }

    internal UseItemOutcome UseHPointCoupon(Client client, int pid, byte mainCell,
        PlayerStore.GenericItem item, ItemCatalog.Item data)
    {
        PlayerStore.GenericItem? remaining = null;
        long qt = _playerStore.GetProgress(pid).QT;
        bool used = false;
        _multiStoreTransaction.Run(() =>
        {
            used = _playerStore.TryUseHPointCoupon(pid, mainCell, item.ItemId, data.GeneralValue,
                out remaining, out qt);
            if (used) SynchronizeQuestPickupObjective(pid, item.ItemId);
        });
        if (!used) return new(ItemResultCode.Error, UseItemResultCode.None);
        client.SendPacket(new RemoveItem { InventoryIndex = InventoryType.Main, Cell = mainCell });
        if (remaining != null) SendInventoryItem(client, remaining);
        client.SendPacket(new Parameter { Param = Parameter.PARAMETER_QT,
            Value = (int)Math.Min(int.MaxValue, qt) });
        return new(ItemResultCode.Ok, UseItemResultCode.None);
    }

    internal UseItemOutcome UsePersonalRespawn(Client client, int pid, byte mainCell,
        PlayerStore.GenericItem item, ItemCatalog.Item data)
    {
        bool used = false;
        if (data.GeneralValue > 0)
            _multiStoreTransaction.Run(() =>
            {
                if (!_playerStore.TryConsumeMainItem(pid, mainCell, item.ItemId, out _)) return;
                _playerStore.AddPersonalRespawns(pid, data.GeneralValue);
                SynchronizeQuestPickupObjective(pid, item.ItemId);
                used = true;
            });
        if (!used) return new(ItemResultCode.Error, UseItemResultCode.Respawn);
        RefreshMainCell(client, pid, mainCell);
        // OnUseItem has no RESPAWN body; the player wrapper only updates this count through
        // HuxleyMMOGame.OnParameter.
        client.SendPacket(new Parameter
        {
            Param = Parameter.PARAMETER_RESPAWNCOUNT,
            Value = _playerStore.GetProgress(pid).RespawnCount
        });
        return new(ItemResultCode.Ok, UseItemResultCode.Respawn);
    }

    internal UseItemOutcome UseBattleRecordReset(Client client, int pid, byte mainCell, PlayerStore.GenericItem item)
    {
        if (_battleResultStore.ForPid(pid).Count == 0)
            return new(ItemResultCode.NoBattlescore, UseItemResultCode.Resetbattlescore);
        bool reset = false;
        _multiStoreTransaction.Run(() =>
        {
            if (!_playerStore.TryConsumeMainItem(pid, mainCell, item.ItemId, out _)) return;
            _battleResultStore.ResetRecord(pid);
            SynchronizeQuestPickupObjective(pid, item.ItemId);
            reset = true;
        });
        if (!reset) return new(ItemResultCode.Error, UseItemResultCode.Resetbattlescore);
        RefreshMainCell(client, pid, mainCell);
        return new(ItemResultCode.Ok, UseItemResultCode.Resetbattlescore);
    }

    // Timed city items (buffs, term licenses, contamination boosters) start a DurativeItemStore entry.
    internal UseItemOutcome UseDurativeItem(Client client, int pid, byte mainCell,
        PlayerStore.GenericItem item, ItemCatalog.Item data)
    {
        bool termLicense = data.ClassName.Contains("HuxleyCityItemData_TermLicense", StringComparison.Ordinal);
        bool replaceContamination = data.ClassName.Contains("HuxleyCityItemData_IncreaseContamination", StringComparison.Ordinal);
        if (termLicense && (_licenseCatalog.Get(data.GeneralValue) == null || _licenseStore.Has(pid, data.GeneralValue)))
            return new(ItemResultCode.CantUseitem, UseItemResultCode.None);
        bool activated = false;
        _multiStoreTransaction.Run(() =>
        {
            if (!_durativeItemStore.TryActivate(pid, data, out var activation,
                    out var replaced, replaceExistingGroup: replaceContamination)) return;
            if (!_playerStore.TryConsumeMainItem(pid, mainCell, item.ItemId, out _))
            {
                _durativeItemStore.RollBackActivation(pid, activation!, replaced);
                return;
            }
            SynchronizeQuestPickupObjective(pid, item.ItemId);
            activated = true;
        });
        if (!activated) return new(ItemResultCode.CantUseitem, UseItemResultCode.None);
        RefreshMainCell(client, pid, mainCell);
        client.SendPacket(new DurativeItemList { Items = _durativeItemStore.Active(pid) });
        if (termLicense)
            client.SendPacket(new ProcLicense
            {
                Plus = true, Result = (byte)LicenseStore.AcquireResult.Ok,
                License = data.GeneralValue
            });
        return new(ItemResultCode.Ok, UseItemResultCode.None);
    }

    internal UseItemOutcome LearnRecipe(Client client, int pid, byte mainCell, PlayerStore.GenericItem item)
    {
        if (_craftCatalog.Get(item.ItemId) == null) return new(ItemResultCode.CantUseitem, UseItemResultCode.Craftrecipe);
        if (_craftStore.Knows(pid, item.ItemId)) return new(ItemResultCode.Alreadylearnedrecipe, UseItemResultCode.Craftrecipe);
        bool learned = false;
        _multiStoreTransaction.Run(() =>
        {
            learned = _playerStore.TryConsumeMainItem(pid, mainCell, item.ItemId, out _)
                && _craftStore.Learn(pid, item.ItemId);
            if (learned) SynchronizeQuestPickupObjective(pid, item.ItemId);
        });
        if (!learned) return new(ItemResultCode.Error, UseItemResultCode.Craftrecipe);
        RefreshMainCell(client, pid, mainCell);
        client.SendPacket(new InsertLearnedRecipe { Recipe = item.ItemId });
        return new(ItemResultCode.Ok, UseItemResultCode.Craftrecipe);
    }

    public static PlayerStore.CraftResultCode CraftResultAfterLearning(PlayerStore.CraftResultCode result) =>
        // CraftMessage resolves SuccessAndLearnRecipe through LuckyItemIndex. A chain-learn
        // recipe with an ordinary MakeItemIndex result still needs BREAK for its item name;
        // InsertLearnedRecipe independently publishes the newly learned recipe.
        result == PlayerStore.CraftResultCode.Success
            ? PlayerStore.CraftResultCode.SuccessAndLearnRecipe
            : result;
}

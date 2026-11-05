using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class CraftHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static CraftCatalog _craftCatalog = null!;
    private static ItemManager _itemManager = null!;
    private static CraftStore _craftStore = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _craftStore = serviceProvider.GetRequiredService<CraftStore>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _craftCatalog = serviceProvider.GetRequiredService<CraftCatalog>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int recipeId) || !reader.TryRead(out byte mainCell)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var recipe = _craftCatalog.Get(recipeId);
        PlayerStore.CraftResultCode result = PlayerStore.CraftResultCode.Error;
        List<byte> changed = [];
        PlayerStore.GenericItem? output = null;
        int learnedRecipe = 0;
        long money = _playerStore.GetProgress(pid).Money;
        if (recipe == null) result = PlayerStore.CraftResultCode.Error;
        else if (!_craftStore.Knows(pid, recipeId)) result = PlayerStore.CraftResultCode.NotLearnedRecipe;
        else
        {
            _multiStoreTransaction.Run(() =>
            {
                int outputId = recipe.MakeItemIndex;
                if (recipe.LuckyItemIndex > 0 && Random.Shared.NextDouble() * 100.0 < recipe.LuckyRate)
                    outputId = recipe.LuckyItemIndex;
                result = _playerStore.TryCraft(pid, recipe, mainCell, outputId, out changed, out output, out money);
                if ((result is PlayerStore.CraftResultCode.Success or PlayerStore.CraftResultCode.Break)
                    && recipe.ChainLearnIndex > 0
                    && Random.Shared.NextDouble() * 100.0 < recipe.ChainLearnRate
                    && _craftStore.Learn(pid, recipe.ChainLearnIndex))
                {
                    learnedRecipe = recipe.ChainLearnIndex;
                    result = ItemManager.CraftResultAfterLearning(result);
                }
                if (result is PlayerStore.CraftResultCode.Success or PlayerStore.CraftResultCode.SuccessAndLearnRecipe
                    or PlayerStore.CraftResultCode.Break)
                    _itemManager.SynchronizeQuestPickupObjectives(pid, recipe.Materials.Select(material => material.ItemId)
                        .Append(recipe.MakeItemIndex).Append(recipe.LuckyItemIndex));
            });
        }
        if (result is PlayerStore.CraftResultCode.Success or PlayerStore.CraftResultCode.SuccessAndLearnRecipe
            or PlayerStore.CraftResultCode.Break)
        {
            foreach (byte cell in changed)
                client.SendPacket(new RemoveItem { InventoryIndex = InventoryType.Main, Cell = cell });
            foreach (byte cell in changed)
            {
                var item = _playerStore.GetInventory(pid, () => [])
                    .FirstOrDefault(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell == cell);
                if (item != null) ItemManager.SendInventoryItem(client, item);
            }
            client.SendPacket(new Parameter
            {
                Param = Parameter.PARAMETER_MONEY, Value = (int)Math.Min(int.MaxValue, money)
            });
            if (learnedRecipe != 0) client.SendPacket(new InsertLearnedRecipe { Recipe = learnedRecipe });
        }
        client.SendPacket(new Craft { Result = (byte)result });
        return true;
    }
}

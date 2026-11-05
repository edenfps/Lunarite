using System.Buffers.Binary;

using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void CraftingAndSockets()
    {
        Check(_craftListWire[0] == 92 && BinaryPrimitives.ReadUInt16LittleEndian(_craftListWire.AsSpan(1)) == 2
            && BinaryPrimitives.ReadInt32LittleEndian(_craftListWire.AsSpan(3)) == 611108
            && BinaryPrimitives.ReadInt32LittleEndian(_craftListWire.AsSpan(7)) == 616400,
            "server op92 recipe list serializes exact word count plus int entries");
        var craftRecipe = _craftCatalog.Get(616400)!;
        var unresolvedCraftOutputs = _craftCatalog.All
            .Where(recipe => _itemCatalog.Get(recipe.MakeItemIndex) == null
                || (recipe.LuckyItemIndex > 0 && _itemCatalog.Get(recipe.LuckyItemIndex) == null)
                || (recipe.ChainLearnIndex > 0 && _craftCatalog.Get(recipe.ChainLearnIndex) == null)
                || recipe.Materials.Any(material => _itemCatalog.Get(material.ItemId) == null))
            .Select(recipe => recipe.EntryIndex).Order().ToArray();
        Check(unresolvedCraftOutputs.Length == 0,
            $"all cooked crafting recipes resolve output, chain, and material IDs; missing: {string.Join(',', unresolvedCraftOutputs.Take(20))}");
        const int craftPid = 920006;
        ResetCharacter(craftPid);
        _playerStore.AddItems(craftPid, craftRecipe.Materials.Select(m => (m.ItemId, m.Count)));
        _playerStore.GetInventory(craftPid, () => craftRecipe.Materials.Select((m, i) => new PlayerStore.GenericItem
            { InventoryIndex = 0, InventoryCell = (byte)i, ItemId = m.ItemId, Count = m.Count }).ToList());
        var craftCode = _playerStore.TryCraft(craftPid, craftRecipe, 0xFF, craftRecipe.MakeItemIndex,
            out var craftChanged, out var craftedItem, out long craftMoney);
        Check(craftCode == PlayerStore.CraftResultCode.Break && craftedItem is { ItemId: 110408, Count: 1, Durability: 100 }
            && craftMoney == 8_800 && craftChanged.Count >= 4
            && _playerStore.GetInventory(craftPid, () => []).All(i => craftRecipe.Materials.All(m => i.ItemId != m.ItemId)),
            "ordinary craft uses native BREAK result while atomically producing full-durability equipment");
        var luckyRecipe = _craftCatalog.Get(611108)!;
        ResetCharacter(craftPid);
        _playerStore.AddItems(craftPid, [(500001, 1)]);
        _playerStore.GetInventory(craftPid, () =>
            [new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 0, ItemId = 500001, Count = 1 }]);
        Check(luckyRecipe is { MakeItemIndex: 500002, LuckyItemIndex: 110301, LuckyRate: 50.0 }
            && _playerStore.TryCraft(craftPid, luckyRecipe, 0xFF, luckyRecipe.LuckyItemIndex,
                out _, out var luckyCraftedItem, out _) == PlayerStore.CraftResultCode.Success
            && luckyCraftedItem?.ItemId == 110301,
            "lucky craft uses native SUCCESS result and inserts the exported lucky item");
        Check(_craftCatalog.Get(611109) is { ChainLearnIndex: 611108, ChainLearnRate: 100.0 },
            "chain-learning recipe retains its exported guaranteed follow-up recipe");
        Check(_craftCatalog.Get(611109) is { LuckyItemIndex: 0 }
            && ItemManager.CraftResultAfterLearning(PlayerStore.CraftResultCode.Break)
                == PlayerStore.CraftResultCode.Break
            && ItemManager.CraftResultAfterLearning(PlayerStore.CraftResultCode.Success)
                == PlayerStore.CraftResultCode.SuccessAndLearnRecipe,
            "chain learning preserves the ordinary craft result when no lucky item exists");
        var craftResultWire = new Craft { Result = (byte)PlayerStore.CraftResultCode.NotEnoughMaterial }.Serialize();
        Check(craftResultWire.SequenceEqual(new byte[] { 91, 8 }),
            "server op91 emits exact RESULTTYPE_CRAFT_NOTENOUGHMATERIAL value");
        var learnedWire = new InsertLearnedRecipe { Recipe = 616401 }.Serialize();
        Check(learnedWire.Length == 5 && learnedWire[0] == 93
            && BinaryPrimitives.ReadInt32LittleEndian(learnedWire.AsSpan(1)) == 616401,
            "server op93 emits the learned chain recipe id");
        var useRecipeWire = new UseItem { Result = 0, UseType = 2 }.Serialize();
        Check(useRecipeWire.SequenceEqual(new byte[] { 79, 0, 2 }),
            "server op79 emits success plus RESULTTYPE_USEITEM_CRAFTRECIPE");
        var recipeUseItem = new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 4, ItemId = 616400, Count = 1 };
        Check(_equipmentRules.ValidateUseItem(craftPid, 4, recipeUseItem, _itemCatalog.Get(616400)) == 0
            && _equipmentRules.ValidateUseItem(craftPid, 4, null, null) == 3
            && new UseItem { Result = 39, UseType = 2 }.Serialize().SequenceEqual(new byte[] { 79, 39 }),
            "city-item use preserves native valid, missing, and already-learned-recipe result branches");
        _characterStore.SetStyle(_equipPid, 3);
        var noSocketLowbodyData = _itemCatalog.Get(200299)!;
        var lowbodyArmorData = _itemCatalog.Get(222717)!;
        var lowbodySkillData = _itemCatalog.Get(332210)!;
        var rankedLowbodySkillData = _itemCatalog.Get(332211)!;
        var equippedLowbody = new PlayerStore.GenericItem
            { InventoryIndex = 4, InventoryCell = 2, ItemId = 222717, Count = 1, Durability = 100 };
        var lowbodySkill = new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 9, ItemId = 332210, Count = 1 };
        var occupiedLowbody = new PlayerStore.GenericItem
            { InventoryIndex = 4, InventoryCell = 2, ItemId = 222717, Count = 1, Durability = 100, Skill = 332210 };
        Check(noSocketLowbodyData.SkillSocketShapes.Count == 0
            && lowbodyArmorData.SkillSocketShapes.SequenceEqual(new[] { "SkillItemShape_Minus" })
            && lowbodySkillData.SkillItemShape == "SkillItemShape_Minus"
            && _equipmentRules.ValidateSkill(_equipPid, 2, 0, 9, 2, equippedLowbody, lowbodySkill,
                lowbodyArmorData, lowbodySkillData) == 0
            && _equipmentRules.ValidateSkill(_equipPid, 1, 0, 9, 1, equippedLowbody, lowbodySkill,
                lowbodyArmorData, lowbodySkillData) == 19,
            "socket-skill equip enforces exported armor slot topology, shape, and ItemPos");
        Check(_equipmentRules.ValidateSkill(_equipPid, 2, 0, 9, 2,
                new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 2, ItemId = 200299, Count = 1, Durability = 100 },
                lowbodySkill, noSocketLowbodyData, lowbodySkillData) == 17
            && _equipmentRules.ValidateSkill(_equipPid, 2, 1, 9, 2, equippedLowbody, lowbodySkill,
                lowbodyArmorData, lowbodySkillData) == 17
            && _equipmentRules.ValidateSkill(_equipPid, 2, 0, 9, 2,
                occupiedLowbody, lowbodySkill, lowbodyArmorData, lowbodySkillData) == 18,
            "socket-skill equip returns native missing-slot and occupied-slot results before mutation");
        Check(rankedLowbodySkillData.EquipRank == 23
            && _equipmentRules.ValidateSkill(_equipPid, 2, 0, 9, 2, equippedLowbody,
                new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 9, ItemId = 332211, Count = 1 },
                lowbodyArmorData, rankedLowbodySkillData) == 41,
            "socket-skill equip parses RANK_SERGEANT11 and returns native insufficient-rank result 41");
        _characterStore.SetStyle(_equipPid, 1);
        ResetCharacter(SkillPid);
        _playerStore.GetInventory(SkillPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 1, ItemId = 200299, Count = 1, Durability = 87 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 9, ItemId = 303400, Count = 1 }
        ]);
    }
}

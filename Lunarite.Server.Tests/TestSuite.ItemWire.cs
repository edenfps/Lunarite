using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

internal sealed partial class TestSuite
{
    // A sale that empties a MAIN cell must reach the client as RemoveItem: the client's UpdateItemIndex(0)
    // leaves its item count up, and enough of those made it refuse later buys and rewards as full.
    private void FullStackSaleClearsCell()
    {
        var previous = _questSocketClient.Player;
        var seller = _characterStore.Create(930821950, "SaleWire", 1, new Custom());
        _playerStore.GetInventory(seller.Pid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 3, ItemId = 604221, Count = 1, Durability = 100 }
        ]);
        _questSocketClient.Player = _playerManager.Get(seller.Pid);
        using (var sellRequest = new PacketWriter())
        {
            sellRequest.Write((byte)65);
            sellRequest.Write((byte)3);
            sellRequest.Write(1);
            Game.OnHandlePacket(_questSocketClient, sellRequest.Buffer);
        }
        byte[] clearWire = ReadQuestSocketPacket();
        ReadQuestSocketPacket(); // money
        ReadQuestSocketPacket(); // undo entry
        byte[] resultWire = ReadQuestSocketPacket();
        Check(clearWire.SequenceEqual(new RemoveItem { InventoryIndex = 0, Cell = 3 }.Serialize())
            && resultWire.SequenceEqual(new SellItem { Result = 0 }.Serialize())
            && !_playerStore.GetInventory(seller.Pid, () => []).Any(item => item.InventoryIndex == 0 && item.InventoryCell == 3),
            "selling a whole stack clears the client's cell with RemoveItem, not UpdateItemIndex(0)");
        _questSocketClient.Player = previous;
    }

    // Saves from before starter skills had a SKILL item: the skill is only on the armor, and taking it
    // off must still work (it failed with Result=1, "Unknown Equip Menu error").
    private void StarterSkillComesOff()
    {
        var owner = _characterStore.Create(930821951, "SkillOff", 1, new Custom());
        _playerStore.GetInventory(owner.Pid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = InventoryType.Armor, InventoryCell = 4, ItemId = 200499, Durability = 100, Skill = 303400 }
        ]);
        bool moved = _playerStore.TryTakeOffSkill(owner.Pid, 4, 0, 0xFF, 0xFF, out byte mainCell, out byte skillCell, out var armor);
        var inventory = _playerStore.GetInventory(owner.Pid, () => []);
        Check(moved && armor is { Skill: 0 } && skillCell == 4
            && inventory.Any(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell == mainCell && i.ItemId == 303400),
            "a skill socketed only on the armor comes off into MAIN");
        Check(_playerStore.GetInventory(owner.Pid, () => []).Single(i => i.ItemId == 200499).Skill == 0,
            "the empty starter boots stay empty, or every read would duplicate the skill");
    }

    // POS_Helmet is EItemPos 0, so cooked helmet skills have no ItemPos; the client treats that as helmet.
    private void HelmetSkillFitsHelmet()
    {
        var owner = _characterStore.Create(930821952, "HelmSkill", 1, new Custom());
        _characterStore.SetStyle(owner.Pid, 2);
        var helmet = new PlayerStore.GenericItem { InventoryIndex = InventoryType.Armor, InventoryCell = 0, ItemId = 201002, Count = 1, Durability = 100 };
        var boots = new PlayerStore.GenericItem { InventoryIndex = InventoryType.Armor, InventoryCell = 4, ItemId = 201402, Count = 1, Durability = 100 };
        var skill = new PlayerStore.GenericItem { InventoryIndex = InventoryType.Main, InventoryCell = 9, ItemId = 301000, Count = 1 };
        Check(_equipmentRules.ValidateSkill(owner.Pid, 0, 0, 9, 0, helmet, skill, _itemCatalog.Get(201002), _itemCatalog.Get(301000)) == 0
            && _equipmentRules.ValidateSkill(owner.Pid, 4, 0, 9, 4, boots, skill, _itemCatalog.Get(201402), _itemCatalog.Get(301000)) == 19,
            "a helmet skill (no cooked ItemPos) goes in a helmet socket and not in boots");
    }

    // Every socket of every armor must accept the skills of its part and shape, by the same rule as
    // HuxleyPCPlayerInventoryMenu.CheckSocketSkillSlot (level and rank gates aside).
    private void EverySocketTakesItsSkills()
    {
        var owner = _characterStore.Create(930821953, "AllSockets", 1, new Custom());
        string[] cells = ["POS_Helmet", "POS_UpBody", "POS_LowBody", "POS_Gloves", "POS_Boots"];
        string[] styles = ["", "CombatStyle_Enforcer", "CombatStyle_Avenger", "CombatStyle_Phantom"];
        string Part(ItemCatalog.Item item) => item.ArmorPart.Length != 0 ? item.ArmorPart : "POS_Helmet";
        var skills = _itemCatalog.All().Where(i => i.ClassName.Contains("HuxleySkillData_", StringComparison.Ordinal)).ToList();
        var armors = _itemCatalog.All().Where(i => i.ArmorPart.Length != 0 && i.SkillSocketShapes.Count != 0).ToList();
        int sockets = 0, pairs = 0;
        var empty = new List<string>();
        var refused = new List<string>();
        foreach (var armor in armors)
        {
            byte cell = (byte)Array.FindIndex(cells, c => c.Equals(armor.ArmorPart, StringComparison.OrdinalIgnoreCase));
            byte style = (byte)Math.Max(0, Array.IndexOf(styles, armor.CombatStyle));
            _characterStore.SetStyle(owner.Pid, style);
            var equipped = new PlayerStore.GenericItem { InventoryIndex = InventoryType.Armor, InventoryCell = cell, ItemId = armor.EntryIndex, Count = 1, Durability = 100 };
            for (byte socket = 0; socket < armor.SkillSocketShapes.Count; socket++)
            {
                sockets++;
                string shape = armor.SkillSocketShapes[socket];
                var fitting = skills.Where(s => s.SkillItemShape == shape
                    && Part(s).Equals(armor.ArmorPart, StringComparison.OrdinalIgnoreCase)
                    && (style == 0 || s.CombatStyle.Length == 0 || s.CombatStyle == "CombatStyle_None" || s.CombatStyle == armor.CombatStyle)).ToList();
                if (fitting.Count == 0) empty.Add($"{armor.EntryIndex}/{socket}");
                foreach (var skill in fitting)
                {
                    pairs++;
                    var source = new PlayerStore.GenericItem { InventoryIndex = InventoryType.Main, InventoryCell = 9, ItemId = skill.EntryIndex, Count = 1 };
                    byte result = _equipmentRules.ValidateSkill(owner.Pid, cell, socket, 9, cell, equipped, source, armor, skill);
                    if (result is not (0 or 40 or 41) || skill.EntryIndex / 100000 != 3) refused.Add($"{armor.EntryIndex}/{socket}+{skill.EntryIndex}={result}");
                }
            }
        }
        Console.WriteLine($"sockets {sockets} on {armors.Count} armors, {pairs} armor/skill pairs, {skills.Count} skills; no skill for: {string.Join(" ", empty.Take(20))}; refused: {string.Join(" ", refused.Take(20))}");
        Check(sockets > 1000 && refused.Count == 0, "every armor socket accepts every skill of its part and shape");
        Check(empty.Count == 0, "every armor socket has at least one skill that fits it");
    }

    // Two players warping together: the one who reappears second must still receive the first.
    private void HiddenPlayerKeepsWhoAppeared()
    {
        var hidden = new Lunarite.World.Player(930821954);
        hidden.NoteMissedPresence(7);
        hidden.NoteMissedPresence(7);
        hidden.NoteMissedPresence(9);
        var missed = hidden.TakeMissedPresence();
        Check(missed.OrderBy(pid => pid).SequenceEqual(new[] { 7, 9 }) && hidden.TakeMissedPresence().Length == 0,
            "a hidden player remembers who appeared meanwhile, once each, until it reappears");
    }

    // Mission wear happens while the client is away: its next Spawn must carry the real durability,
    // through op217 because the client refuses op82 for an occupied slot.
    private void WornGearReachesClientAtSpawn()
    {
        var owner = _characterStore.Create(930821955, "WornGear", 1, new Custom());
        _playerStore.GetInventory(owner.Pid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = InventoryType.Weapon, InventoryCell = 0, ItemId = 100502, Count = 1, Durability = 23 }
        ]);
        _questSocketClient.Session.QueueEquipmentRefresh();
        _questManager.FlushQuestTravelRefresh(_questSocketClient, owner.Pid);
        byte[] wire = ReadQuestSocketPacket();
        Check(wire.SequenceEqual(new ItemReplacement
            {
                RemovedInventory = InventoryType.Weapon, RemovedCell = 0,
                Item = new InsertItem { ItemID = 100502, Count = 1, InventoryIndex = InventoryType.Weapon, Cell = 0, Durability = 23 }
            }.Serialize())
            && !_questSocketClient.Session.TryTakeEquipmentRefresh(),
            "worn equipment reaches the client once, by op217, at its next city Spawn");
    }

    // CMMOVerify::AddDatas copies BELT, WEAPON, ARMOR and SKILL into the mission: supplies travel too.
    private void SuppliesTravelWithTheLoadout()
    {
        var owner = _characterStore.Create(930821956, "BeltCarrier", 1, new Custom());
        _playerStore.GetInventory(owner.Pid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = InventoryType.Belt, InventoryCell = 0, ItemId = 510001, Count = 5, Durability = 100 },
            new PlayerStore.GenericItem { InventoryIndex = InventoryType.Main, InventoryCell = 0, ItemId = 510001, Count = 3, Durability = 100 }
        ]);
        var loadout = _itemManager.StartingLoadoutItems(owner.Pid);
        Check(loadout.Count == 1 && loadout[0] is { ItemID: 510001, Count: 5, InvenIndex: InventoryType.Belt, Cell: 0 },
            "equipped supplies, not the main inventory, go into missions and battles");
        var quest = _questCatalog.Get(846000)!;
        Check(_questInstanceManager.BuildQuestCreate(owner.Pid, quest, 0, new byte[16]).Items.Count == 0,
            "a mission host's op42 leaves supplies out");
    }

    private void OutsideNoticeIsTakenOnce()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"huxley-notice-{Guid.NewGuid():N}.txt");
        System.IO.File.WriteAllText(path, " Restart in 1 minute ");
        Check(Lunarite.World.OnlineStatus.TakeNotice(path) == "Restart in 1 minute"
            && Lunarite.World.OnlineStatus.TakeNotice(path) == null && !System.IO.File.Exists(path)
            && Lunarite.World.OnlineStatus.NoticePath("x/online.json").EndsWith("notice.txt"),
            "a notice file is broadcast once and removed");
    }
}

using Microsoft.Extensions.DependencyInjection;
using System.Buffers.Binary;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

internal sealed partial class TestSuite
{
    private void TuneCatalogAndBpLevels()
    {
        const int legacyLootMigrationPid = 920071;
        ResetCharacter(legacyLootMigrationPid);
        _playerStore.AddItems(legacyLootMigrationPid, [(604221, 2), (608001, 1), (111201, 1)]);
        int legacyMigrationNow = checked((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var migratedLegacyInventory = _itemManager.BuildInitialInventory(legacyLootMigrationPid)
            .Where(x => x.InventoryIndex == 0).ToList();
        Check(migratedLegacyInventory.Single(x => x.ItemId == 604221) is { Count: 2, Durability: 0 }
            && migratedLegacyInventory.Single(x => x.ItemId == 608001).State == 0x02
            && migratedLegacyInventory.Single(x => x.ItemId == 111201).LimitTime
                >= legacyMigrationNow + 1440 * 60,
            "legacy earned-loot migration applies cooked durability, pickup binding, and possession timers");
        Check(_itemCatalog.Get(128403) is { TuneupFee: 1500, TuneupLunaritesA: 14 }
            && _itemCatalog.Get(672001) is { ResetTuneupAll: true }
            && _itemCatalog.Get(672002) is { ResetTuneupPart: true }
            && _itemCatalog.Get(673001) is { RemoveItemBind: true },
            "exported item catalog identifies tune costs and exact reset/bind consumable classes");
        Check(_itemCatalog.Get(128403)?.TuneupOptionCaps.SequenceEqual(new[] { 4, 4, 4, 4, 4, 4, 4 }) == true
            && _itemCatalog.Get(232012)?.TuneupOptionCaps.SequenceEqual(new[] { 5, 5, 5, 5, 5, 5, 5 }) == true,
            "exported item catalog recovers weapon and armor tune-up categories with per-option caps");
        Check(new[] { 230404, 231108, 231009 }.All(id =>
                _itemCatalog.Get(id)?.TuneupOptionCaps.SequenceEqual(new[] { 4, 4, 4, 4, 4, 4, 4 }) == true)
            && _itemCatalog.Get(230404) is { TuneupLunaritesA: 14, TuneupFee: 2500 },
            "armor without its own tune-up categories inherits the HuxleyArmorData defaults (7 options, cap 4)");
        Check(_questCatalog.Get(860101) is { UseQuestRoom: true, MaxLife: -1, DurabilityDecParam: 9 }
            && _questCatalog.Get(846100) is { UseQuestRoom: false, MaxLife: 3 }
            && _battleZoneCatalog.Get(138) is { MaxPlayers: 32, GoalScore: 10, TimeLimitMinutes: 10 }
            && _battleZoneCatalog.Get(214) is { MaxPlayers: 44, GoalScore: 8000, TimeLimitMinutes: 45 },
            "quest and battle-zone fields left out of the export take the class defaults");
        var auctionMachineGun = _itemCatalog.Get(128403);
        var auctionHelmet = _itemCatalog.Get(210001);
        var auctionSkill = _itemCatalog.Get(303400);
        var auctionNormal = _itemCatalog.Get(604221);
        Check(auctionMachineGun is { AuctionCategory: 3, ItemGrade: 4 }
            && auctionHelmet is { AuctionCategory: 9, CombatStyle: "CombatStyle_Enforcer" }
            && auctionSkill is { AuctionCategory: 24 }
            && auctionNormal is { AuctionCategory: 26 },
            $"exported item classes map to native categories (actual {auctionMachineGun?.AuctionCategory}/{auctionHelmet?.AuctionCategory}/{auctionSkill?.AuctionCategory}/{auctionNormal?.AuctionCategory})");
        Check(PlayerStore.ComputePlayerLevelupGoalBP(1) == 112
            && PlayerStore.ComputePlayerLevelupGoalBP(9) == 16800
            && PlayerStore.ComputePlayerLevelupGoalBP(29) == 506520,
            "retail BP level thresholds match the exported formula and subparameters");
        ResetCharacter(LevelPid);
        Check(_playerStore.AddReward(LevelPid, 0, 112, 0).Level == 1,
            "retail strict threshold keeps exactly 112 BP at level 1");
        var levelTwo = _playerStore.AddReward(LevelPid, 0, 1, 0);
        Check(levelTwo.Level == 2 && levelTwo.LP == 1,
            "crossing 112 BP reaches level 2 and grants one level-up LP");
        _classB = _playerStore.AddReward(LevelPid, 0, PlayerStore.ComputePlayerLevelupGoalBP(9) + 1 - levelTwo.BP, 0);
    }

    private void LootTables()
    {
        Check(_questLoot.RollForEnemy(int.MaxValue, 0) is null,
            "unknown enemy identities cannot produce fabricated fallback loot");
        // Boss sprays: every boss in the table can drop its band's colors at about 8 %, and nothing else gets them.
        var sprayRolls = Enumerable.Range(0, 4000).SelectMany(_ => _questLoot.RollForEnemy(9021118, 0) ?? [])
            .Where(drop => drop.item / 1000 == 664).ToList();
        var allSprays = Enumerable.Range(664001, 23).ToHashSet();
        var offered = QuestLoot.BossSprays.Values.SelectMany(x => x).ToHashSet();
        Check(sprayRolls.Count is > 200 and < 450 && sprayRolls.All(d => d.num == 1 && QuestLoot.BossSprays[9021118].Contains(d.item))
                && _questLoot.EnemyCanDrop(9021118, 664007, 0) && !_questLoot.EnemyCanDrop(9021118, 664023, 0)
                && allSprays.Except(offered).OrderBy(x => x).SequenceEqual([664001])
                && QuestLoot.BossSprays.Keys.All(id => _questLoot.EnemyLevel(id) > 0),
            "bosses drop one spray of their level band at about 8 %; every spray color except retail's Red has a boss");
        // Crafting: every material any recipe uses, and every schematic no vendor sells, drops from some level band.
        var craftDrops = _services.GetRequiredService<CraftDrops>();
        var craftCatalog = _services.GetRequiredService<CraftCatalog>();
        var itemGroups = _services.GetRequiredService<ItemGroupCatalog>();
        var bandTiers = new[] { 'B', 'A', 'S' }.Select(craftDrops.ForTier).ToList();
        var droppedMaterials = bandTiers.SelectMany(t => t!.Materials.Select(m => m.Item)).ToHashSet();
        var droppedSchematics = bandTiers.SelectMany(t => t!.Schematics).ToHashSet();
        var usedMaterials = craftCatalog.All.Where(r => r.EntryIndex is not (611108 or 611109))
            .SelectMany(r => r.Materials).Select(m => m.ItemId).Where(id => id / 1000 % 10 == 3 && id / 100000 == 6).ToHashSet();
        var unsold = craftCatalog.All.Select(r => r.EntryIndex)
            .Where(id => id is not (611108 or 611109 or 616300) && !itemGroups.IsVendorItem(id)).ToHashSet();
        // 9191549 Jackal is level 15 (B band).
        var craftRolls = Enumerable.Range(0, 5000).SelectMany(_ => _questLoot.RollForEnemy(9191549, 0) ?? [])
            .Where(d => CraftDrops.TierOf(d.item) == 'B' && d.item / 1000 % 10 == 3).ToList();
        Check(usedMaterials.Count >= 70 && usedMaterials.IsSubsetOf(droppedMaterials)
                && unsold.Count > 100 && unsold.SetEquals(droppedSchematics)
                && craftRolls.Count is > 750 and < 1250
                && craftRolls.All(d => d.num == CraftDrops.QuantityOf(d.item))
                && _questLoot.EnemyCanDrop(9191549, 613005, 0) && !_questLoot.EnemyCanDrop(9191549, 633005, 0)
                && CraftDrops.TierForLevel(9) == '?' && CraftDrops.TierForLevel(25) == 'A' && CraftDrops.TierForLevel(31) == 'S',
            "enemies of level 10+ drop their tier's crafting materials (~20 %) and unsold schematics (~1 %); every part and every unsold schematic has a source");
        QuestLootGrants.ClearInstance(923001);
        QuestLootGrants.Issue(923001, 923002, [(603056, 2), (603056, 1)]);
        Check(!QuestLootGrants.TryClaim(923001, 923003, [(603056, 1)], () => true)
            && !QuestLootGrants.TryClaim(923001, 923002, [(603056, 4)], () => true)
            && !QuestLootGrants.TryClaim(923001, 923002, [(603056, 4)], () => true)
            && !QuestLootGrants.TryClaim(923001, 923002, [(603056, 2)], () => false)
            && QuestLootGrants.TryClaim(923001, 923002, [(603056, 1), (603056, 2)], () => true)
            && !QuestLootGrants.TryClaim(923001, 923002, [(603056, 1)], () => true),
            "quest pickup grants are participant-bound, aggregate duplicate rows, and consume only after commit");
        QuestLootGrants.Issue(923001, 923002, [(603056, 1)]);
        QuestLootGrants.ClearInstance(923001);
        Check(!QuestLootGrants.TryClaim(923001, 923002, [(603056, 1)], () => true),
            "next quest run cannot inherit unclaimed pickup grants from a prior instance");
        // Host leaves first: the guest is sent back at once and its mission-end op48 lands after
        // that (live log 2026-11-03 12:57:57, PID 14 rejected). It still claims its own drops.
        const int lootHostPid = 923101, lootGuestPid = 923102, lootHostClient = 923201, lootGuestClient = 923202;
        QuestLootGrants.ClearInstance(lootHostPid);
        _questInstanceManager.SetHost(lootHostPid, lootHostPid);
        _questRoomMembers.SetInstance(lootHostClient, lootHostPid);
        _questRoomMembers.SetInstance(lootGuestClient, lootHostPid);
        QuestLootGrants.Issue(lootHostPid, lootHostPid, [(604019, 4)]);
        QuestLootGrants.Issue(lootHostPid, lootGuestPid, [(604020, 2)]);
        _questInstanceManager.LeaveQuestInstance(lootHostClient, lootHostPid);
        _questInstanceManager.LeaveQuestInstance(lootGuestClient, lootGuestPid);
        bool lateClaim = _questInstanceManager.TryResolveLootClaim(lootGuestClient, lootGuestPid,
                out int claimInstance, out int claimPool)
            && claimInstance == lootHostPid && claimPool == lootGuestPid
            && !QuestLootGrants.TryClaim(claimInstance, claimPool, [(604019, 4)], () => true)
            && QuestLootGrants.TryClaim(claimInstance, claimPool, [(604020, 2)], () => true);
        bool expired = !_questInstanceManager.TryResolveLootClaim(lootGuestClient, lootGuestPid, out _, out _,
            DateTime.UtcNow.Ticks + Lunarite.World.QuestInstanceManager.LootClaimGrace.Ticks + 1);
        bool stranger = !_questInstanceManager.TryResolveLootClaim(923203, 923103, out _, out _);
        Check(lateClaim && expired && stranger,
            "guest pickups claimed after the host left draw from the guest's own drops for a short grace period only");
        var lootReferences = _questLoot.AuditReferences();
        Console.WriteLine($"Loot audit: enemies={lootReferences.Enemies}, groups={lootReferences.Groups}, "
            + $"references={lootReferences.EnemyGroupReferences}, missing=[{string.Join(',', lootReferences.MissingGroups)}], "
            + $"invalid=[{string.Join(',', lootReferences.InvalidGroups)}], "
            + $"missingReachableItems=[{string.Join(',', lootReferences.MissingReachableItems)}], "
            + $"missingUnreferencedItems=[{string.Join(',', lootReferences.MissingUnreferencedItems)}]");
        Check(lootReferences.Enemies == 936 && lootReferences.Groups > 0 && lootReferences.EnemyGroupReferences > 0
            && lootReferences.MissingGroups.Count == 0 && lootReferences.InvalidGroups.Count == 0
            && lootReferences.MissingReachableItems.Count == 0,
            "all reachable enemy and scripted loot-group references and item IDs resolve to retail definitions");
        Check(_itemCatalog.Get(90110001)?.ClassName == "HuxleyCityItemData_ItemColorSpray",
            "normal item table MasterIndex resolves child item IDs");
        Check(Enumerable.Range(90110001, 23).All(id =>
                _itemCatalog.Get(id) is { ClassName: "HuxleyCityItemData_ItemColorSpray" })
            && _itemCatalog.Get(90110100) is { ClassName: "HuxleyBattleItemData", MaxCollapseCount: 500 },
            "all 24 normal-table definitions resolve under their retail full item IDs");
        Check(_questLoot.HasGroup(49110002) && !_questLoot.HasGroup(10002)
            && _questLoot.Roll(49110002, 0).Any(drop => drop.item == 90110001),
            "etc drop-table MasterIndex resolves the retail group ID and its guaranteed item");
        // Group 80007 lists six 33% special items (SpecialDropCount=1) at difficulty 0; equal
        // entries must come out about equally, not favour list order.
        var specialCounts = Enumerable.Range(0, 6000).SelectMany(_ => _questLoot.Roll(80007, 0))
            .GroupBy(drop => drop.item).ToDictionary(g => g.Key, g => g.Count());
        int firstSpecial = specialCounts.GetValueOrDefault(100101), lastSpecial = specialCounts.GetValueOrDefault(202103);
        Check(firstSpecial > 0 && lastSpecial > 0 && lastSpecial > firstSpecial * 0.7
            && specialCounts.Where(pair => pair.Key is 300030 or 302040 or 301000).All(pair => pair.Value == 0),
            $"special drops pick among hits without list-order bias (first {firstSpecial}, sixth {lastSpecial})");
        const int normalSprayPid = 920199;
        ResetCharacter(normalSprayPid);
        _playerStore.GetInventory(normalSprayPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 3, ItemId = 90110001, Count = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 1, ItemId = 200199, Count = 1, Durability = 100 }
        ]);
        Check(_itemCatalog.Get(90110001) is { ClassName: "HuxleyCityItemData_ItemColorSpray" }
            && !_playerStore.TryUseColorSpray(normalSprayPid, 3, 90110001, out _, out _)
            && _playerStore.GetInventory(normalSprayPid, () => []).Any(x => x.ItemId == 90110001),
            "a color spray outside the client's 16-bit spray range is refused and kept");
        // HuxleySeqAct_SpawnItem in cooked quest maps calls HuxleyItemFactory.RequestItem(GroupIndex),
        // which sends op57 directly. These references are absent from the enemy->group table audited
        // above. A scan of 68 .war packages found 19 literal IDs plus 16 bound through linked
        // SeqVar_Int/Named/External variables (quest pickups such as Tarconium Red and Pluto ores).
        int[] scriptedPickupGroups =
        [
            80107, 80108, 81136, 81137, 82161, 82162, 82163, 82164, 82165, 82166,
            82167, 82168, 82169, 83101, 83127, 83128, 83129, 83130, 83232,
            82020, 82021, 82022, 83028, 83029, 83143, 83144, 83145, 83146, 83147,
            83148, 83149, 83150, 83151, 83341, 83342
        ];
        Check(scriptedPickupGroups.Length == QuestLoot.ScriptedGroupCount
            && scriptedPickupGroups.All(group => _questLoot.HasGroup(group) && QuestLoot.IsScriptedGroup(group)),
            "op57 allows exactly the cooked scripted item-factory groups and all resolve to retail drops");
        var conditionCoverage = _questConditions.AuditCoverage();
        Check(conditionCoverage.Rules == 530
            && conditionCoverage.UnsupportedConditions.Count == 0
            && conditionCoverage.UnsupportedProperties.Count == 0,
            "all 530 exported quest gates use fully supported condition and player-property classes");
        _recommendedLevelCharacter = _characterStore.Create(930846105, "RecommendedLevelGate", 1, new Custom());
        _characterStore.SetStyle(_recommendedLevelCharacter.Pid, 1);
        ResetCharacter(_recommendedLevelCharacter.Pid);
    }

    private void BatchInsertPackets()
    {
        byte[] insertDropsWire = new byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(insertDropsWire, 2);
        BinaryPrimitives.WriteInt32LittleEndian(insertDropsWire.AsSpan(4), 604221);
        BinaryPrimitives.WriteInt32LittleEndian(insertDropsWire.AsSpan(8), 3);
        BinaryPrimitives.WriteInt32LittleEndian(insertDropsWire.AsSpan(12), 100100);
        BinaryPrimitives.WriteInt32LittleEndian(insertDropsWire.AsSpan(16), 1);
        var insertDropsReader = new PacketReader(insertDropsWire);
        Check(QuestInsertDropItemAtOnceRequest.TryDeserialize(ref insertDropsReader, out var insertDrops)
            && insertDrops.Items.SequenceEqual(new[] { (604221, 3), (100100, 1) }),
            "native op48 count plus packed item/quantity pairs parses exactly");
        var insertDropsAnswer = new InsertDropItemAtOnceQuest { Answer = 0, Items = insertDrops!.Items }.Serialize();
        Check(insertDropsAnswer[0] == 51 && insertDropsAnswer[1] == 0
            && BinaryPrimitives.ReadInt32LittleEndian(insertDropsAnswer.AsSpan(2)) == 2
            && insertDropsAnswer.AsSpan(6).SequenceEqual(insertDropsWire.AsSpan(4)),
            "op51 batch-insert acknowledgment echoes the native bd+pair payload");
    }

    private void ObjectiveReportsAndCompletion()
    {
        Check(_questStore.Acquire(FieldEventPid, 871801)
            && _questStore.RecordFieldEvent(FieldEventPid, "FQOType_PickItem", 603006, 2).Single().Count == 2
            && _questStore.RecordFieldEvent(FieldEventPid, "FQOType_PickItem", 603006, 1).Single().Count == 3
            && _questStore.ReadyToComplete(FieldEventPid, 871801),
            "exact quest-item pickups advance matching exported field objectives");
        const int initialPickupPid = 920095;
        _questStore.Reset(initialPickupPid);
        Check(_questStore.Acquire(initialPickupPid, 871801)
            && _questStore.InitializePickItemObjectives(initialPickupPid, 871801,
                itemIndex => itemIndex == 603006 ? 8 : 0).Single()
                is { QuestIndex: 871801, ObjectiveIndex: 0, Count: 3 }
            && _questStore.ReadyToComplete(initialPickupPid, 871801),
            "field pickup objectives initialize from existing main-inventory counts and cap at the retail target");
        const int synchronizedPickupPid = 920086;
        _questStore.Reset(synchronizedPickupPid);
        Check(_questStore.Acquire(synchronizedPickupPid, 871801)
            && _questStore.SynchronizePickItemObjectives(synchronizedPickupPid, 603006, 2).Single().Count == 2
            && _questStore.SynchronizePickItemObjectives(synchronizedPickupPid, 603006, 2).Count == 0
            && _questStore.GetProgress(synchronizedPickupPid, 871801).Objectives[0].Count == 2,
            "field pickup synchronization uses absolute MAIN totals instead of compounding insert deltas");
        Check(_questStore.SynchronizePickItemObjectives(synchronizedPickupPid, 603006, 3).Single().Count == 3
            && _questStore.ReadyToComplete(synchronizedPickupPid, 871801)
            && _questStore.SynchronizePickItemObjectives(synchronizedPickupPid, 603006, 0).Single().Count == 0
            && !_questStore.ReadyToComplete(synchronizedPickupPid, 871801),
            "field pickup synchronization tracks possession loss and revokes premature reward readiness");
        Check(_questStore.RecordFieldEvent(FieldEventPid, "FQOType_PickItem", 999999).Count == 0
            && _questStore.RecordFieldEvent(FieldEventPid, "unknown", 603006).Count == 0,
            "field objective fallback ignores unrelated targets and unsupported event types");
        Check(_questCatalog.All.SelectMany(x => x.Objectives).Count(x => x.BattleZoneObjective == "FQOType_KillEnemy") == 104
            && _questCatalog.All.SelectMany(x => x.Objectives).Count(x => x.BattleZoneObjective == "FQOType_PickItem") == 154,
            "field-event progression covers every exported exact-enemy and exact-item objective");
        var weaponObjectiveQuests = _questCatalog.All
            .Where(quest => quest.Objectives.Any(objective => objective.BattleZoneObjective == "FQOType_Kills_ByWeapon"))
            .ToArray();
        bool allWeaponObjectivesAccepted = weaponObjectiveQuests.Length == 12;
        for (int i = 0; i < weaponObjectiveQuests.Length; i++)
        {
            var quest = weaponObjectiveQuests[i];
            var objective = quest.Objectives.Single(x => x.BattleZoneObjective == "FQOType_Kills_ByWeapon");
            int pid = 921000 + i;
            _questStore.Reset(pid);
            allWeaponObjectivesAccepted &= objective.ExtraParam > 0
                && objective.ExtraParam2 is 1 or 5 or 6 or 7
                && _questStore.Acquire(pid, quest.EntryIndex)
                && _questStore.UpdateObjective(pid, quest.EntryIndex, objective.Index, objective.CompleteCount, true)
                && _questStore.ReadyToComplete(pid, quest.EntryIndex);
        }
        Check(allWeaponObjectivesAccepted,
            "all 12 exact enemy-and-weapon objectives accept the authoritative op55 absolute report");
        Check(!_questStore.UpdateObjective(QuestPid, 999999, 0, 1, true),
            "reports for unacquired quests are rejected");
        const int cityQuestPid = 920092;
        Check(_questStore.Acquire(cityQuestPid, 820202)
            && _questStore.UpdateObjective(cityQuestPid, 820202, 0, 1, true)
            && _questStore.ReadyToComplete(cityQuestPid, 820202)
            && _questStore.UpdateObjective(cityQuestPid, 820202, 0, 0, true)
            && !_questStore.ReadyToComplete(cityQuestPid, 820202)
            && _questStore.UpdateObjective(cityQuestPid, 820202, 0, 1, true)
            && _questStore.ReadyToComplete(cityQuestPid, 820202)
            && _questStore.Complete(cityQuestPid, 820202),
            "absolute city objective reports grant and revoke reward readiness without rally telemetry");
        Check(_questStore.Complete(QuestPid, QuestIndex), "quest completion succeeds");
        Check(!_questStore.Complete(QuestPid, QuestIndex),
            "duplicate quest completion cannot consume items or grant rewards again");
        Check(_questStore.GetProgress(QuestPid, QuestIndex).Objectives.Count == 0,
            "completion removes transient quest progress");
        ResetCharacter(DeletedPid);
        _playerStore.GetInventory(DeletedPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 0, ItemId = 128403, Count = 1 }
        ]);
        _licenseStore.Grant(DeletedPid, 710021);
        _craftStore.Learn(DeletedPid, 616400);
        _personalBuffStore.Put(DeletedPid, new PersonalBuff { ID = 77, Level = 1 });
        _durativeItemStore.ReplaceAll(DeletedPid,
        [
            new DurativeItem { ItemID = 88, ExpirationDate = int.MaxValue }
        ]);
        _questStore.Acquire(DeletedPid, 820102);
        _battleResultStore.Add(new BattleResultRequest(DeletedPid, 0, 0, new byte[27], new byte[12],
            new byte[48], new byte[104]));
        _transmitStore.Add(DeletedPid, SurvivingPid, 0, "survivor", "owned", "mail");
        _deliveredMailSequence = _transmitStore.Add(SurvivingPid, DeletedPid, 0, "deleted",
            "reference", "mail", cr: 25, attachments:
            [new PlayerStore.GenericItem { ItemId = 603056, Count = 2 }]).Sequence;
    }
}

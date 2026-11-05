using System.Buffers.Binary;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

internal sealed partial class TestSuite
{
    private void ObjectiveWireFormat()
    {
        byte[] objectiveWire = new byte[11];
        BinaryPrimitives.WriteInt32LittleEndian(objectiveWire, 920001);
        BinaryPrimitives.WriteInt32LittleEndian(objectiveWire.AsSpan(4), 820102);
        objectiveWire[8] = 2;
        objectiveWire[9] = 7;
        objectiveWire[10] = 1;
        var objectiveReader = new PacketReader(objectiveWire);
        Check(QuestListenObjectiveRequest.TryDeserialize(ref objectiveReader, out var objectiveReport)
            && objectiveReport.Pid == 920001 && objectiveReport.QuestIndex == 820102
            && objectiveReport.ObjectiveIndex == 2 && objectiveReport.Count == 7 && objectiveReport.Visible == 1,
            "native op55 dd/byte/byte/byte objective wire format parses exactly");
    }

    private void QuestResultAndPostData()
    {
        byte[] questResultWire = new byte[196];
        BinaryPrimitives.WriteInt32LittleEndian(questResultWire, 920001);
        questResultWire[4] = 9;
        for (byte i = 0; i < 4; i++)
        {
            questResultWire[5 + i * 3] = 3;
            questResultWire[6 + i * 3] = i;
            questResultWire[7 + i * 3] = (byte)(90 - i);
        }
        for (byte i = 0; i < 5; i++)
        {
            int offset = 5 + (4 + i) * 3;
            questResultWire[offset] = 4;
            questResultWire[offset + 1] = i;
            questResultWire[offset + 2] = (byte)(80 - i);
        }
        questResultWire[32] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(questResultWire.AsSpan(33), 17);
        questResultWire[35] = 7;
        BinaryPrimitives.WriteUInt16LittleEndian(questResultWire.AsSpan(36), 300);
        var questResultReader = new PacketReader(questResultWire);
        Check(QuestResultRequest.TryDeserialize(ref questResultReader, out var questResult)
            && questResult.Pid == 920001 && questResult.Equipment.Count == 9
            && questResult.Equipment[0] == new QuestResultRequest.EquipmentDurability(3, 0, 90)
            && questResult.Equipment[8] == new QuestResultRequest.EquipmentDurability(4, 4, 76)
            && questResult.WeaponUses[0] == new QuestResultRequest.WeaponUse(1, 17)
            && questResult.WeaponUses[1] == new QuestResultRequest.WeaponUse(7, 300)
            && questResult.WeaponUseData.Length == 12 && questResult.Summary.Length == 48
            && questResult.Detail.Length == 104,
            "native op60 dbmmmm quest-result telemetry parses exact fixed block sizes");

        _questBackupWire = new PacketWriter();
        _questBackupWire.Write((byte)0); _questBackupWire.Write((byte)2);
        _questBackupWire.Write(920001); _questBackupWire.Write((ushort)1234); _questBackupWire.Write((byte)2);
        _questBackupWire.Write(604221); _questBackupWire.Write(3);
        _questBackupWire.Write(613995); _questBackupWire.Write(1);
        var questBackupReader = new PacketReader(_questBackupWire.Buffer);
        Check(QuestGameDataRequest.TryDeserialize(ref questBackupReader, out var questBackup)
            && questBackup.Index == 0 && questBackup.Total == 2 && questBackup.Pid == 920001
            && questBackup.EncodedLevelBp == 1234
            && questBackup.Items.SequenceEqual(new[]
            {
                new QuestGameDataRequest.StockItem(604221, 3),
                new QuestGameDataRequest.StockItem(613995, 1)
            }),
            "native op152 bbdwbm retains the exported eight-byte stock-item records");
        var shortBackupReader = new PacketReader(_questBackupWire.Buffer.AsSpan(0, _questBackupWire.Buffer.Length - 1));
        Check(!QuestGameDataRequest.TryDeserialize(ref shortBackupReader, out _),
            "op152 rejects a truncated stock-item array instead of accepting partial backup data");

        var questResultAnswer = new ReceivedQuestResult
        {
            Rank = 2,
            Items = [(604221, 3), (110105, 1)]
        }.Serialize();
        Check(questResultAnswer.Length == 19 && questResultAnswer[0] == 62
            && questResultAnswer[1] == 2 && questResultAnswer[2] == 2
            && BinaryPrimitives.ReadInt32LittleEndian(questResultAnswer.AsSpan(3)) == 604221
            && BinaryPrimitives.ReadInt32LittleEndian(questResultAnswer.AsSpan(7)) == 3
            && BinaryPrimitives.ReadInt32LittleEndian(questResultAnswer.AsSpan(11)) == 110105
            && BinaryPrimitives.ReadInt32LittleEndian(questResultAnswer.AsSpan(15)) == 1,
            "op62 quest-result answer serializes native bb + BunchOfItems blocks");
        Check(new ReceivedQuestResult { Rank = 6 }.Serialize().SequenceEqual(new byte[] { 62, 6, 0 }),
            "op62 accepts the native failure rank and an empty result-item array");

        var questAppearance = new Custom
        {
            Hair = 1, Color = 2, Skin = 3, Eye1 = 4, Eye2 = 5, Eye3 = 6,
            Nose1 = 7, Nose2 = 8, Nose3 = 9, Nose4 = 10,
            Mouth1 = 11, Mouth2 = 12, Mouth3 = 13, Jaws1 = 14, Jaws2 = 15,
            Face1 = 16, Face2 = 17, EyeBrow1 = 18, EyeBrow2 = 19, EyeBrow3 = 20,
            EyeBrow4 = 21, MakeUp = 22, Personality = 23, PersonalityX = 24, PersonalityY = 25
        };
        _questCreateAppearance = new QuestCreate
        {
            Answer = 0, Type = 4, HostMap = "Q-SP_Begin?Master", PlayerName = "Appearance", Appearance = questAppearance,
            MinPlayers = 4, Difficulty = 2,
            PlayerMoney = 1234567, PlayerLevel = 17, PlayerStyle = 2, PlayerRank = 3,
            PlayerBP = 2345678, PlayerLP = 0x20304050, PersonalRespawn = 7,
            DurativeItems = [new DurativeItem
            {
                ItemID = 650001, OptionValue = 2, ExpirationDate = int.MaxValue, AddInfo = 9, CityItemGroup = 3
            }],
            PersonalBuffs = [new PersonalBuff
            {
                ID = 7654321, Group = 2, ModifierType = 3, CanBeCumulated = 1, Level = 4,
                ExpirationDate = 123456789
            }],
            Licenses = [710021],
            Items = [new QuestCreate.Item
            {
                ItemID = 200499, Count = 1, InvenIndex = 4, Cell = 4, State = 2,
                LimitTime = 7, TuneUpOptions = 8, Skill0 = 303400, Skill1 = 9, Durability = 100
            }]
        }.Serialize();
        Check(_questCreateAppearance.AsSpan().IndexOf("Q-SP_Begin?Master\0"u8) >= 0,
            "op42 host map carries the native world-level Master option needed by the gateway JOIN bypass");
        int questHostOffset = _questCreateAppearance.AsSpan().IndexOf("Q-SP_Begin?Master\0"u8);
        int questGateOffset = questHostOffset + "Q-SP_Begin?Master\0"u8.Length + 16;
        Check(_questCreateAppearance[questGateOffset] == 4
            && _questCreateAppearance[questGateOffset + 1] == 2,
            "op42 carries the selected retail minimum-player and difficulty bytes into native quest travel");
        int appearanceOffset = _questCreateAppearance.AsSpan().IndexOf(
            Enumerable.Range(1, 25).Select(x => (byte)x).ToArray());
        Check(appearanceOffset >= 4
            && BinaryPrimitives.ReadInt32LittleEndian(_questCreateAppearance.AsSpan(appearanceOffset - 4)) == 1234567,
            "op42 embeds persisted Custom bytes in the exact HuxleyBattleGame.PostData appearance block");
        int charInfoOffset = appearanceOffset + 25;
        int pointInfoOffset = charInfoOffset + 20;
        Check(BinaryPrimitives.ReadUInt16LittleEndian(_questCreateAppearance.AsSpan(charInfoOffset)) == 17
            && _questCreateAppearance[charInfoOffset + 2] == 2
            && _questCreateAppearance[charInfoOffset + 3] == 3
            && BinaryPrimitives.ReadInt32LittleEndian(_questCreateAppearance.AsSpan(pointInfoOffset)) == 2345678
            && BinaryPrimitives.ReadInt32LittleEndian(_questCreateAppearance.AsSpan(pointInfoOffset + 4)) == 0x20304050,
            "op42 carries native money, CharInfo style/rank, and BP/LP mission state");
        byte[] expectedProperty = [7, 0, 0, 0, 1, 1, 0, 0];
        Check(_questCreateAppearance.AsSpan().IndexOf(expectedProperty) >= 0,
            "op42 carries respawn and retail base inventory/bank expansion steps");
    }

    private void ItemAndTravelAnswers()
    {
        byte[] leaveWire = [0xFF, 0xFF, 0xFF, 0xFF];
        var leaveReader = new PacketReader(leaveWire);
        Check(QuestLeaveRequest.TryDeserialize(ref leaveReader, out var leave)
            && leave.ZoneIndex == -1 && leave.AreaIndex == -1,
            "native op41 ww quest-leave request parses retail return-to-city sentinels");
        byte[] explicitLeaveWire = [14, 0, 3, 0];
        var explicitLeaveReader = new PacketReader(explicitLeaveWire);
        Check(QuestLeaveRequest.TryDeserialize(ref explicitLeaveReader, out var explicitLeave)
            && explicitLeave.ZoneIndex == 14 && explicitLeave.AreaIndex == 3,
            "native op41 ww quest-leave request preserves explicit city zone and area");
        Check(new LoadMap { Index = 10, Position = -1 }.Serialize().SequenceEqual(new byte[] { 19, 10, 255 })
            && new LoadMap { Index = 10, Position = 13 }.Serialize().SequenceEqual(new byte[] { 19, 10, 13 }),
            "server op19 uses native byte map/area fields and preserves INDEX_NONE as 0xFF");
        byte[] spawnWire = Convert.FromHexString("0000C84428D3E3C4DFA7064300000000CC7F000000000000");
        var spawnReader = new PacketReader(spawnWire);
        Check(SpawnRequest.TryDeserialize(ref spawnReader, out var spawn)
            && Math.Abs(spawn.X - 1600f) < 0.01f && spawn.Pitch == 0
            && spawn.Yaw == 32716 && spawn.Roll == 0,
            "native client op17 spawn parses exact vector plus Unreal rotator fffddd body");

        byte[] itemPairWire = [0x3D, 0x38, 0x09, 0x00, 0x02, 0x00, 0x00, 0x00];
        var itemPairReader = new PacketReader(itemPairWire);
        Check(QuestItemRequest.TryDeserialize(ref itemPairReader, out var itemPair)
            && itemPair.ItemIndex == 604221 && itemPair.Quantity == 2,
            "native quest item dd body parses exactly");
        var singleDropAnswer = new InsertDropItemQuest { Answer = 0, ItemIndex = 604221, Quantity = 2 }.Serialize();
        Check(singleDropAnswer.Length == 10 && singleDropAnswer[0] == 50 && singleDropAnswer[1] == 0
            && BinaryPrimitives.ReadInt32LittleEndian(singleDropAnswer.AsSpan(2)) == 604221
            && BinaryPrimitives.ReadInt32LittleEndian(singleDropAnswer.AsSpan(6)) == 2,
            "server op50 single-drop answer serializes exact bdd body");

        var equipArmorAnswer = new EquipArmor { Result = 0, ArmorCell = 3, MainCell = 11 }.Serialize();
        Check(equipArmorAnswer.SequenceEqual(new byte[] { 67, 0, 3, 11 }),
            "server op67 armor-equip answer serializes native success body");

        Check(new EquipBelt { Result = 0, MainCell = 12 }.Serialize()
                .SequenceEqual(new byte[] { 70, 0, 12 }),
            "server op70 belt-equip answer serializes native success body");
        var takeOffArmorWire = new TakeOffArmor
        {
            Result = 0, ArmorCell = 4, ItemID = 200499, Count = 1, MainCell = 15,
            Skill0 = 321003, Durability = 87
        }.Serialize();
        Check(takeOffArmorWire.Length == 35 && takeOffArmorWire[0] == 71 && takeOffArmorWire[1] == 0
            && takeOffArmorWire[2] == 4
            && BinaryPrimitives.ReadInt32LittleEndian(takeOffArmorWire.AsSpan(3)) == 200499
            && takeOffArmorWire[11] == 0 && takeOffArmorWire[12] == 15
            && BinaryPrimitives.ReadInt32LittleEndian(takeOffArmorWire.AsSpan(23)) == 321003
            && BinaryPrimitives.ReadInt32LittleEndian(takeOffArmorWire.AsSpan(31)) == 87,
            "server op71 armor-takeoff answer serializes exact byte + 32-byte GenericItem body");
        Check(new TakeOffWeapon { Result = 0, WeaponCell = 2, MainCell = 13 }.Serialize()
                .SequenceEqual(new byte[] { 72, 0, 2, 13, 255 }),
            "server op72 weapon-takeoff answer serializes native success body");
        Check(new TakeOffBelt { Result = 0, MainCell = 14 }.Serialize()
                .SequenceEqual(new byte[] { 73, 0, 14, 255 }),
            "server op73 belt-takeoff answer serializes native success body");
        Check(new SwapItemInSameInven { Result = 0, Inventory = 0, SourceCell = 7, TargetCell = 9 }.Serialize()
                .SequenceEqual(new byte[] { 77, 0, 0, 7, 9 })
            && new SwapItemInSameInven { Result = 31 }.Serialize().SequenceEqual(new byte[] { 77, 31 }),
            "server op77 serializes native swap and authoritative-refresh result forms");
        Check(new DropItem { Result = 0 }.Serialize().SequenceEqual(new byte[] { 66, 0 })
            && new ItemArrange { Result = 0 }.Serialize().SequenceEqual(new byte[] { 94, 0 }),
            "drop and item-arrange responses serialize exact native one-byte results");
        Check(new BuyItem { Result = 0 }.Serialize().SequenceEqual(new byte[] { 64, 0 })
            && new SellItem { Result = 0 }.Serialize().SequenceEqual(new byte[] { 65, 0 }),
            "buy and sell responses serialize exact native one-byte results");
        Check(new InsertBank { Result = 0, MainCell = 7, BankCell = 2 }.Serialize()
                .SequenceEqual(new byte[] { 86, 0, 7, 2 })
            && new TakeoutBank { Result = 0, BankCell = 2, MainCell = 7 }.Serialize()
                .SequenceEqual(new byte[] { 87, 0, 2, 7 }),
            "bank deposit/withdraw responses serialize native result and cell order");
        ResetCharacter(TakeoffPid);
        _playerStore.GetInventory(TakeoffPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 1, ItemId = 100499 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 8, ItemId = 604221 }
        ]);
        Check(_playerStore.TryTakeOff(TakeoffPid, 3, 1, 8, out byte takeoffMain, out byte relocatedMain)
            && takeoffMain == 8 && relocatedMain == 0
            && _playerStore.GetInventory(TakeoffPid, () => []).Any(i => i.InventoryIndex == 0 && i.InventoryCell == 8 && i.ItemId == 100499)
            && _playerStore.GetInventory(TakeoffPid, () => []).Any(i => i.InventoryIndex == 0 && i.InventoryCell == 0 && i.ItemId == 604221),
            "takeoff atomically relocates an occupied MAIN target using the native response cells");
        Check(_playerStore.TryMoveWithinInventory(TakeoffPid, 0, 0, 2, 0,
                out bool wholeRefresh, out _, out var movedWhole)
            && !wholeRefresh && movedWhole?.ItemId == 604221,
            "same-inventory whole-item move uses native local-swap result semantics");
        Check(_playerStore.TryAddQuestItems(TakeoffPid, [(604221, 5)], out _),
            "quest item insert commits aggregate and slotted persistence together");
        var stack = _playerStore.GetInventory(TakeoffPid, () => []).First(i => i.InventoryIndex == 0 && i.ItemId == 604221 && i.Count == 5);
        Check(_playerStore.TryMoveWithinInventory(TakeoffPid, 0, stack.InventoryCell, 6, 2,
                out bool splitRefresh, out var splitSource, out var splitTarget)
            && splitRefresh && splitSource?.Count == 3 && splitTarget?.Count == 2,
            "same-inventory counted move splits stacks for op81/op82 authoritative refresh");
        Check(_playerStore.TryDropItem(TakeoffPid, 0, 6, 1, out var dropRemaining)
            && dropRemaining?.Count == 1
            && _playerStore.GetItems(TakeoffPid).First(x => x.item == 604221).num == 4,
            "partial item drop decrements aggregate and slotted persistence");
        Check(_playerStore.ArrangeMainInventory(TakeoffPid, out var preArrangeCells, out var arrangedItems)
            && preArrangeCells.Count == arrangedItems.Count
            && arrangedItems.Select(i => (int)i.InventoryCell).SequenceEqual(Enumerable.Range(0, arrangedItems.Count)),
            "item arrange compacts MAIN cells for authoritative refresh");
        long vendorStart = _playerStore.GetProgress(TakeoffPid).Money;
        _vendorData = _itemCatalog.Get(604221)!;
        Check(_playerStore.TryBuyItem(TakeoffPid, _vendorData, 20, 2, out var boughtItem, out _afterBuy, out _)
            && boughtItem is { InventoryCell: 20, Count: 2 } && _afterBuy == vendorStart - 256,
            "vendor buy atomically applies retail price and persistent inventory");
        ResetCharacter(QtBuyerPid);
        _playerStore.GetInventory(QtBuyerPid, () => []);
        var qtWeapon = _itemCatalog.Get(138401)!;
        long qtBuyerMoney = _playerStore.GetProgress(QtBuyerPid).Money;
        _playerStore.GetProgress(QtBuyerPid).QT = 999;
        Check(!_playerStore.TryBuyItem(QtBuyerPid, qtWeapon, 0, 1, out _, out _, out byte qtFailure)
            && qtFailure == 14 && _playerStore.GetProgress(QtBuyerPid).QT == 999,
            "QT-only vendor purchase rejects an insufficient QT balance without mutation");
        _playerStore.GetProgress(QtBuyerPid).QT = 1000;
        Check(_playerStore.TryAddQuestItems(QtBuyerPid, [(609001, 28)], out _)
            && _playerStore.TryBuyItem(QtBuyerPid, qtWeapon, 10, 1, out var qtPurchase, out long qtMoney, out byte qtSuccess)
            && qtSuccess == 0 && qtPurchase?.ItemId == 138401
            && _playerStore.GetProgress(QtBuyerPid).QT == 0 && qtMoney == qtBuyerMoney,
            "QT-only vendor purchase consumes exported QTPrice instead of CR");
        Check(!_playerStore.TryBuyItem(QtBuyerPid, _itemCatalog.Get(140601)!, 1, 1,
                out _, out _, out byte gmFailure) && gmFailure == 7,
            "ordinary characters cannot purchase exported GM-only items");
        Check(!_playerStore.TryBuyItem(QtBuyerPid, _itemCatalog.Get(120396)!, 1, 1,
                out _, out _, out byte disabledFailure) && disabledFailure == 7,
            "vendor purchase rejects cooked bEnabled=false items hidden by both retail trade menus");
        _pcRoomMoney = _playerStore.GetProgress(QtBuyerPid).Money;
    }
}

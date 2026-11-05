# Quest native wire audit (US-client, 2010-06-21)

Authoritative target: `HuxleyMMOGame_2010-06-21.exe - ai.i64`. Native wrappers were correlated
with the declaration order and parameter shapes in exported `HuxleySubsystem.uc`, then their send
helpers were decompiled directly. This supersedes the earlier guessed symmetric opcode mapping.

| Script native | Exec wrapper | Send helper | Client opcode and exact format |
|---|---:|---:|---|
| `SendQuestListenRallyPoint` | `0x11145B80` | `0x111C47C0` | 54, `ddbbbdddddd` |
| `SendQuestListenObjective` | `0x11145DC0` | `0x111C4850` | 55, `ddbbb` |
| `SendQuestListenSpawn` | `0x11145F10` | `0x111C4950` | 61, `d` |
| `SendQuestResult` | `0x111460A0` | direct native implementation | complex result path |
| `SendPlayerQuestResult` | `0x111460D0` | `0x111CCF40` | 60, `dbmmmm` after player-state assembly |
| `SendQuestEnemydown` | `0x11149480` | `0x111C2CB0` | 56, `d` |
| `SendDropItemGroup` | `0x111494E0` | `0x111C2CF0` | 57, `d` |
| `SendQuestInsertDropItemAtOnce` | native wrapper | `0x111C7310` | 48, `dm` |

## Payloads

Opcode 54 arguments are emitted in this order:

`PID, QuestIndex, RallyPoint, MaxRallyPoint, Complete, SquadBP, BP, QT, KilledMonsters, SpawnedMonsters, PlayTime`

The first two and final six values are 32-bit integers. The three middle values are bytes. The
`SquadBP` position is non-obvious: it is transmitted before `BP` and `QT`, despite appearing last
among the optional UnrealScript arguments.

Opcode 55 is:

`PID:int32, QuestIndex:int32, ObjectiveIndex:byte, Count:byte, Visible:byte`

Although `ObjectiveIndex` and `Count` are declared as UnrealScript `int`, the native helper narrows
both to bytes on the wire.

Opcode 56 carries the quest-enemy data `EntryIndex`. Opcode 57 carries a baked item-drop-group
index. `HuxleyEnemyCharacter.RequestItem` arms `Delegate_DropItem` for the opcode-59 reply;
`HuxleyItemFactory.RequestItem` arms `Delegate_DropItem_Group` for the opcode-60 reply.

Opcode 48 is `SendQuestInsertDropItemAtOnce`, not `SendQuestResult`. It sends an `int32` count and
one raw eight-byte `(ItemIndex:int32, Num:int32)` block per entry. Server opcode 51 answers with
`Answer:byte, Count:int32`, followed by the same pairs; the client invokes
`OnInsertDropItem_AtOnce_Quest` and releases its item-transaction lock. Actual `SendQuestResult`
uses client opcode 60 with format `dbmmmm` and fixed blob lengths 27, 12, 48, and 104.

The op60 header is `PID:int32, EquipmentRecordCount:byte`; the client always supplies count 9.
The 27-byte block is nine `(Inventory:byte, Cell:byte, Durability:byte)` records: four weapon
slots (`Inventory=3`) followed by five armor slots (`Inventory=4`). The remaining blocks contain
12 bytes of weapon-use counters, a 48-byte summary, and 104 bytes of detailed quest statistics.
The Lunarite server currently validates the entire fixed shape and persists the nine durability records.

Use `tools/ida_decompile.py` to reproduce address-level decompilation from the existing IDA
database. The IDA database is analysis input only and must never be launched as the game client.
## Quest-result callback timing

`HuxleyQuestGame.FinishRallyPoint` calls `SendQuestResult()` first, then each controller's
`RallyPointFinished()`. For quests with `ResultDrop`, that function invokes the reliable client RPC
`ClientAcquireQuestResult`, which calls native `RequestQuestResult(QuestIndex, callback)`.
Native helper `0x111C5870` does not send a request: it only stores the three-word Unreal delegate at
`UHuxleySubsystem + 793..795`. The MMO server therefore responds to client opcode 60 asynchronously
with opcode 62; the intervening socket round trip gives the script time to arm the callback.

Client parser `0x1112EF30` reads opcode 62 as `"bb"` (`QuestRank`, item count), followed by
`count * 8` bytes. Each eight-byte block is `BunchOfItems { int nItemID; int nQuantity; }`. The
client supports ranks 0 through 5 (`HuxleyRandomRewardMenu.RankCoord[6]`). The menu cannot close
with zero items because `MoveToInven()` returns before starting the insert transaction.

### Shipped-table activation audit

`QuestExtractor questresultdata` loads each build's `HuxleyGame.u` before inspecting its quest
table, including `Default__HuxleyQuestData` and the raw `UDefaultProperty` list of every quest:

- June US-client: 530 `HuxleyQuestData` objects; 0 result-field overrides.
- April Korean: 516 `HuxleyQuestData` objects; 0 result-field overrides.
- Both class default objects leave `ResultDrop=false`, all `DropGrade_*` values at zero, and every
  `TimeLimits[]` value at zero (zero-valued defaults are omitted from UE3 default-property data).

Consequently `RallyPointFinished()` never executes `ClientAcquireQuestResult()` with either shipped
table. Faithful server behavior for these builds is to consume opcode 60 and not send opcode 62.
Opcode 62 belongs to a dormant/other-content-revision feature; it is implemented for wire fidelity
but must not be activated without a quest table that explicitly sets `ResultDrop=true`.

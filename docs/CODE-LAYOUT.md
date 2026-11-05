# Code layout (Lunarite server)

How the server is organised after the 2026-11-02 architecture pass. The structure follows
[Sanctuary](https://github.com/Open-Source-Free-Realms/Sanctuary): static packet handler
classes that take their services in `ConfigureServices(IServiceProvider)`, and everything that
holds state is a DI singleton with constructor-injected dependencies and an `ILogger`.

## Folders

| Folder (namespace) | What lives there |
|---|---|
| `Handlers/` (`Lunarite.Handlers`) | The three opcode dispatchers (`Game`, `Agent`, `BattleBackend`) and one handler class per packet in `Handlers/<Link>/<Feature>/<Name>Handler.cs`. |
| `World/` (`Lunarite.World`) | Runtime state and game rules: `Player`/`PlayerManager`, one manager per feature (`SquadManager`, `TradeManager`, `QuestInstanceManager`, ...), battle instances, `EquipmentRules`, `EconomyRates`, `QuestConditions`, `QuestRoomMembers`. Large managers split into partials by concern (`SimulationManager.Rooms.cs`, `.Match.cs`). |
| `Storage/` (`Lunarite.Storage`) | Persistent stores (`PlayerStore`, `CharacterStore`, `QuestStore`, ...) and the persistence layer under them (`SqliteStore`, `AtomicJson`, `MultiStoreTransaction`, `StateBackup`). |
| `Resources/` (`Lunarite.Resources`) | Read-only tables loaded from `data/`: `ItemCatalog`, `QuestCatalog`, `CityZoneCatalog`, `BattleZoneCatalog`, `QuestRewards`, `QuestLoot`, `CashShopCatalog`, ... |
| `Network/` (`Lunarite.Network`) | `Server`, `Client`, `ClientSession`, framing, packet reader/writer, the P2P NAT test service, the patch server, the cash-shop web server, the quest gateway (UE3 handshake research). |
| `Packets/` | `Incoming/` (client -> server), `Outgoing/` (server -> client), `Common/` (shared structs and the generated client enums). One packet per file. |

`ServiceCollectionExtensions.AddServerServices()` registers every service;
`ConfigurePacketHandlers()` calls `ConfigureServices` on every static class that has one.
`Program` builds the container, runs transaction recovery and the legacy import before anything
resolves a store, then configures the handlers and starts the listeners.

## Packets and handlers

Packet classes carry the client's native names:

- Server -> client: the client receive handler the packet feeds, without `On`
  (`OnParameter` -> `Parameter`, `OnQuestCheck` -> `QuestCheck`,
  `OnSwapItem_InSameInven` -> `SwapItemInSameInven`, Simulation Center -> `JoinRoom`,
  `ReadyCancelBC`, ...). Where the client handler fires no event, its `CClientTcpLink::Proc*`
  name is used (`ProcPeerAddrList` -> `PeerAddrList`).
- Client -> server: the native sender plus `Request` (`SendQuestCheck` -> `QuestCheckRequest`).
- Packets whose opcode is a field (squad, clan, auction, friend, block and LFS action results)
  and the few with no native name keep descriptive names. `analysis/audit/opcode-coverage.md`
  has the full opcode table.

Handlers are named after the native sender too (`SendJoinRoom` -> `JoinRoomHandler`,
`_SendMsgGetLicense` -> `GetLicenseHandler`). The opcode `switch` in each dispatcher is the
protocol table. A handler parses its packet, checks it, and calls the feature manager. A Game
link starts with `ConnectHandler` (op0) and ends with `DisconnectHandler`, which `Server` calls
when the link closes.

## Players and connections

- `Client.Session` (`ClientSession`) holds what ends with the connection: the authenticated
  account, P2P reports, travel flags, pending purchase and quest-enter handshakes, menu pages.
- `Client.Player` is the selected character. `PlayerManager` keeps one `Player` per PID (city
  zone, position, spawned flag, tram ride, mounted vehicle, undo list, ...), the PID -> link
  registry of online characters and the list of authenticated links.
- `QuestRoomMembers` keeps quest-room membership (quest, instance, difficulty, detached links)
  keyed by connection, because a character's old city link keeps its quest room while it travels
  into the instance.

## Managers

Each manager owns its state and keeps it private; other code calls its methods. Operations that
must run under a manager's lock are methods on that manager, never `lock (other._lock)` from
outside. Handlers parse their packet and pass the manager plain values (PIDs, indexes, parsed
request records), not a `PacketReader`. Dependencies point one way: handlers call managers,
managers call lower managers and stores. Leaving a quest is `QuestInstanceManager.QuestLeave`;
inventory-driven quest objectives are `ItemManager.SynchronizeQuestPickupObjective(s)`.

## Names

Types and fields that mirror a client struct use its member names in PascalCase without the
Hungarian prefix: `PlayerProgress.BP`/`LP`/`QT`/`SkillSlot` (`nBP`, `nLP`, `nQT`, `nSkillSlot`),
`QuestStore.Progress.SquadBP`, `GenericItem.InventoryIndex`/`InventoryCell`. Saved JSON keeps the
older property names through `[JsonPropertyName]`, so existing data loads unchanged. Two-letter
acronyms are upper case (`BP`, `QT`, `XP`), longer ones PascalCase (`Pid`).

Manager operations that answer one client request carry that request's native sender name
without `Send` (`SendSquadCreate` -> `SquadManager.SquadCreate`, `SendQuestLeave` ->
`QuestInstanceManager.QuestLeave`, `SendTradeAddItem` -> `TradeManager.TradeAddItemLocked`).
Methods that reimplement a client script function take its name (`CheckStartCondition`,
`GetTribe`, `HaveWeaponLicense`, `GetEquipLevel`, `IsOpenedWeaponSlot`,
`ComputePlayerLevelupGoalBP`). Server-only logic with no client counterpart keeps a descriptive
name.

## Codes and constants

Answer bytes and indexes use names, never bare numbers:

- `Packets/Common/ClientEnums.cs` is generated from the client's `HuxleySubsystem.uc` and
  `HuxleyItemSystem.uc` (`tools/codegen/client_enums.py`): `ItemResultCode`, `QuestResultCode`,
  `SimulationAnswer`, `ClanResultCode`, `InventoryType`, ...
- `Packets/Common/MessageAnswers.cs` names the answers the client only shows as text.
- Packet-local tables sit next to their packet (`TradeAction`, `TradeItemResult`,
  `ServerMessageKind`).

## Data tables

`Resources/*Catalog.cs` read the exported UE3 tables in `data/`. Each record is read through a
property-name -> reader table (`Fields`) into a `Pending` object whose fields carry the UE property
names, then built into the immutable record.

## Logging

Everything logs through `ILogger`. `LogProvider` sends it to the console and the optional log
file (`HUXLEY_LOG_DIR`) through the `Log` sink.

## Tests

`Lunarite.Server.Tests` runs with `dotnet run -c Debug` and prints one `PASS:` line per check.
`TestSuite.cs` holds the shared fixtures (the service container, test accounts and characters,
the encrypted Game socket, ...), the helpers (`Check`, `ReadQuestSocketPacket`, ...) and the run
order; each feature's checks live in `TestSuite.<Feature>.cs`, in chapter methods named after
what they check. Checks run in the listed order because later ones build on state earlier ones
leave behind. Tests reach server internals through `internal` members (`InternalsVisibleTo`),
never reflection.


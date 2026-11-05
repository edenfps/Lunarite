# Feature and authority inventory (2026-09-22)

This is a map of the game exposed by the exported UnrealScript, cooked tables, the US-client native send/receive audits, and the current private server. It is **not** a claim that every menu or packet works in live play. `Client` means the retail client or its player-hosted UE3 mission process performs the behavior. `Server` means durable/shared/authoritative state must be recreated by the Lunarite server. `Both` means the client provides presentation or simulation while the server authorizes, synchronizes, or persists it.

Status: **Live** = representative client flow observed; **Built** = implemented and tested/static-audited, with limited live proof; **Partial** = material known gap; **Deferred** = explicitly outside current server work. A native opcode route is only a wire surface, not feature completion. See [COMPLETION.md](COMPLETION.md) for the evidence-weighted completion estimate.

| Feature family | Client does | Server must do | Current status / next proof |
|---|---|---|---|
| Login and account | Login/registration UI, error display | Credentials, duplicate-session policy, Agent→Game handoff, account isolation | **Built/Live** core login; recovery, bans, and broader multi-account cases unverified. |
| Character roster | Creation/selection/appearance UI, faction selection | Names, slots, species/style/appearance, deletion/restoration, per-account roster | **Built/Live** core roster; full cross-faction/style matrix not validated. |
| World presentation | Render maps, materials, lighting, UI, HUD, dialogue, NPCs | Provide correct destination/zone/area and player state; not render assets | **Client**, with compatibility-layer work ongoing; asset correctness is separate from server protocol. |
| City travel and subway | Cooked station/zone list and destination choice, loading/travel UI | Validate requested city/area, persist resume point, route entry, charge warp cost if applicable | **Built/Live** some travel; all routes and zone unlock policy need coverage. |
| City presence | Local pawn rendering, movement and animation | Register location, broadcast player add/move/leave, appearance/equipment/vehicle state | **Built/Live** basic presence; equipment/vehicle observer edge cases need live checks. |
| Personal vehicles | Summon/dismiss/control/visuals | Ownership, item level/rank checks, item use, shared vehicle state and observer publication | **Built**, observer transition timing needs live validation. Presence exit now clears the old pawn's mount before a destination observer snapshot, not only on socket disconnect. `HuxleyCityPersonalVehicle` overrides `DriverEnter` and `CanEnterVehicle` (the latter returns true); its personal-vehicle summon path does not use the separate `HuxleyVehicle.IsDrivingLicensePossessed` gate for battle vehicles. Do not impose that license on city hover bikes. |

| NPC/vendor dialogue | Dialogue and shop menus, table-based item display | Buy/sell/undo, prices, money, inventory transaction | **Built**, only limited live vendor paths. |
| Character combat in PvE | Weapon firing, movement, damage presentation and much mission combat simulation | Persist equipment/ammo/rewards and validate reports relevant to durable outcomes | **Both**; first quest path live, wider weapon/enemy cases unverified. |
| PvE mission objectives | Mission map, Kismet sequences, objective HUD, enemy spawning/AI, rally and checkpoint behavior | Admit players, record authoritative objective/result reports, restore journal, settle result | **Partial**: solo path live; varied mission types and report sequencing need evidence. The exported active Kismet action reports objectives through client op55, not the commented gateway objective path. |
| PvE instance/party entry | Player-hosted UE3 listen-server travel, loading, match simulation | Party invite/accept, admission/permission, instance allocation, transfer, return and host-loss handling | **Partial**: packet paths built; multi-player entry remains a reported live failure. |

The dev binary's native server-event 44 handler `OnQuestEnterRequest` at `0x1110FFF0`
deserializes exactly `"dd"` (key and quest index) before dispatching
`HUXLEYGAME_OnQuestEnterRequest`. The exported
`HuxleyPCMMOGame.OnQuestEnterRequest` script declares a third byte,
`byDifficultyLevel`, and indexes the difficulty-label array with it. The existing
server `QuestEnterRequest` serializer matches the proven native wire shape but has
no production caller. Do not emit event 44 as a speculative squad-entry fix: the
native-to-script difficulty argument's source/default and the original master's
dispatch trigger remain unproven. This static mismatch is independent of the
observed guest package-loader crash after mission travel.
| PvE loot | Local kill/drop presentation | Use cooked enemy/drop groups, choose/award item quantities, prevent duplicate settlement, persist pickups | **Built/Live** basic drops; retail selection weighting and broad enemy cases not proven. |
| Quest journal | Quest menus, map markers, text, local objective display | Availability/conditions, acquire/share/retire/complete, progress restoration | **Built/Live** first chain; representative coverage across 530 quests remains. |
| Quest rewards | Reward UI and optional-choice selection | Apply cooked BP/LP/CR/item modifiers exactly once; persist and publish refresh | **Built/Live** first rewards; complete choice/failure matrix unverified. |
| Character progression | Level/rank/skill/attribute UI, effects | BP/LP/CR/level/rank mutation, unlock checks, class promotion publication | **Built**, limited live UI validation. |
| Inventory/equipment | Slots, drag/drop, equip visuals, item tooltips | Exact item instances, moves/splits/equip, binding, durability, expiry, reconnect restoration | **Built/Live** core gear; catalog repairability now follows the exported armor/weapon subclass hierarchy, including helmet and upper-body armor, so newly acquired gear starts at 100 durability. Full item/slot/style matrix remains unverified. |
| Skills and socketing | Activation effects, five-slot HUD and local action | Skill ownership, socket placement/permutation, eligibility and persisted loadout | **Built/Live** tutorial skill; broader skill table unverified. |
| Licenses | Requirement displays and license menus | Permanent/temporary ownership, eligibility checks and expiration | **Built**, live purchase/use coverage limited. |
| Repair/tune-up/crafting | Menus, preview and animations | Material/CR cost, result choice, item mutation and durable transaction | **Built**; exact original tune-up weighting remains unknown. |
| Bank/storage | Storage UI | Capacity, deposit/withdraw, inventory limits, persistent ownership | **Built**, limited live validation. |
| Mail/transmit | Inbox, composition, attachment UI | Delivery, pagination, read/delete, money/item attachment transfer, notification | **Built/Live** inbox display; encrypted Game-socket tests cover named-recipient send and once-only item/CR claims, but live claim UI and edge cases remain unverified. |
| Auction | Listings/search/sale UI | Escrow, listing, purchase, expiry/returns and seller settlement | **Built**; encrypted register/bid/settlement and timed-out escrow-return tests pass, live-client UI unverified. |
| Direct trade | Offer/confirm UI | Two-player atomic exchange, money and binding checks | **Built**; request, acceptance and commit require both players spawned in the same city. A PID can have only one pending or active exchange, so two accepted prompts cannot overwrite a live session. Cancellation notifies only recorded counterparts, never an arbitrary client-supplied PID. Fully repaired gear is tradeable; damaged repairable gear is not, matching the exported menu's native `IsRepairable` check. Live two-client validation remains incomplete. |
| Cash shop | In-client catalog/store UI, package imagery | Catalog, free entitlement/purchase/gift/claim and item effects | **Built** as private-server free catalog; external retail billing is intentionally absent. Visual asset parity is separate. |

The cooked `HuxleyItemTable` has 14 distinct `HuxleyCityItemData_*` subclasses. Eleven use the server's op77 item-use path (including recipes, expansions, coupons, color spray, respawn charges and timed effects); `ItemLicenseResetter` uses op86, `ResetTuneup` uses op198, and `RemoveItemBind` uses op201. This is a static route audit, not proof that every item variant works in the live UI. No additional cooked city-item subclass is currently available to implement from this table.

The exported `HuxleyPCMMOHUD.OpenPubScene` and `ClosePubScene` functions are inside a block comment, and no `HuxleyPubMenu` class is in the exported client scripts. The controller retains debug-style `ExecBuyDrink`/`ExecPubSystemResult` calls and the cooked personal-buff setters exist, but those alone do not establish a reachable drink minigame in this build. The server's gauge-result calculation is provisional; do not count the pub UI as live or tune its outcome formula without a reachable client path or native evidence.
| Chat/friends/blocks | Chat and social menus | Routing, presence, contact/block lists, flood policy | **Built**, proximity and reconnect behavior need live checks. Encrypted roster tests cover city-online, quest-detached online, and disconnected states. |
| Squads/LFS | Invite/accept/formation/directory UI | Membership, leader/permissions, directory, party mission admission | **Partial** because mission co-entry is not live working despite structural squad routes. |
| Clans | Creation/member/permission/medal UI | Membership, ranks, permissions, persistence, notices | **Built** structurally; member rosters show detached quest characters in Quest state. Retail election/war and broad live flows remain unverified. |
| Persistence/operations | Client reconnect and state refresh UI | Atomic character/account stores, cross-store transaction recovery, backups, migrations, security | **Partial**: local durability tests exist; off-host backup and operational hardening remain. |
| PvP/Simulation Center | Dedicated battle/room gameplay and UI | Separate room, admission, scoring, result authority | **Deferred** by user; no active server implementation. Do not reuse removed work. |

Vehicle observer ordering needs a correlated client trace before another server change.
Exported `HuxleyMMOGame.OnUpdateVehicle` forwards op175 to
`HuxleyMMOAnotherPlayer.OnSummonCityVehicle`, which returns immediately if its
Pawn is absent. With a Pawn, the method does not assign an absolute mount state:
it summons the supplied vehicle only when `DrivenVehicle == None`, otherwise it
dismounts regardless of the supplied item ID. `OnAddPlayer` separately calls that
same method once when its initial `nVehicleIndex` is nonzero. The server currently
sends mounted item ID in the add-player snapshot and op175 on each summon toggle.
Thus a missed, duplicated, or reordered callback can invert an observer's view;
the reported "mount before entering area" symptom cannot identify which boundary
failed. In the next two-client run, correlate server op20/op175 order with the
observer's add-player/Pawn creation and local DrivenVehicle transitions. Sending
an extra op175 as a speculative refresh is unsafe because it is a toggle.

The exported `HuxleySubwayMenu.OnClick` resolves the selected station caption through
`FindCityZoneByAreaName`, takes that zone's index and `GetAreaIndex`, then calls
`HuxleyMMOPlayerController.MoveMap`. That controller sends native `SendMoveMap` for a
different zone and teleports locally within the same zone. Neither path checks an
unlock flag or deducts a fare. The supplied `HuxleyCityZoneData` defines zone type,
level and areas but no subway price or unlock field. Thus a conjectured station fare
or level gate is not a supported server feature for this client; retain the existing
faction/zone/area/map validation and seek a distinct active caller before adding one.

The active `HuxleyAuctionItemSellMenu.OnSellButtonClick` calls
`SendAuctionRegister(ItemCellIndex, SellPrice, 0, OnAuctionRegister)`; the UI exposes no
period selection. The native send declaration carries a bid-period byte, but that
wire field alone does not establish multiple listing durations in this build. The
server accepts only that active-menu zero value and uses a 24-hour listing lifetime;
nonzero requests fail before CR or escrow moves. Do not add selectable duration
behavior or claim the 24-hour policy is exact retail timing
without an original-server trace or another active client caller.

The cooked city-zone table names two ordinary building maps absent from both supplied client installs:
Sapiens zone 98 (`City-HQ-Lobby`) and zone 99 (`City-ShoppingMall-Lobby`). Direct travel now
rejects those destinations rather than sending a map load that cannot complete. This does not
restore the missing map content; quest-room/tutorial zone types retain their separate paths.

## What the evidence actually establishes

The shipped `HuxleyPCMMOHUD.OpenAuctionMenu` sends `SendAuctionReturnAll` before opening the
auction UI. The sell menu sends `SendAuctionRegister` with period byte zero. The current
request-driven timeout return and period-zero registration are therefore supported by this
client path; a speculative background return or alternate listing duration is not warranted
without a different active caller or live packet evidence.

- Native binary audits enumerate **151/151 routed active non-PvP constant client sends** and **197/197 emittable active functional server callbacks**; **187/197** currently have a production runtime reference. This establishes protocol *surface*, not wire timing, correct state transitions, or live end-to-end behavior. Run `tools/protocol-coverage.ps1` and `tools/server-event-coverage.ps1`. The ten serializer-only callbacks are opcodes 5, 44, 78, 116, 124, 125, 194, 206, 222, and 235; none should be emitted solely to raise this metric without evidence for its trigger and authority.
- Exported UnrealScript identifies local versus network-facing methods. Mission hosting and gateway boundaries are visible in `HuxleyGame/Classes/HuxleyQuestGame.uc`, `HuxleyQuestMasterServerGateway.uc`, and `Engine/Classes/HuxleyMasterServerGateway.uc`; city UI and requests are in `HuxleyMMOGame/Classes`, including `HuxleySubwayMenu.uc` and the auction, bank, craft, clan, and quest menus. A UI class alone proves an intended feature, **not** that its server behavior existed or is now correct.
- Cooked tables under `data/tables` establish data, not implementation: 530 quest records and reward modifiers, city zones, items, enemies/drop groups, licenses, skills and paid products. A table entry alone does not prove it is reachable or purchasable in this client build.
- The separate `HuxleyQuestItemGroupData` table's 149 entries are NPC quest-menu groups, despite the `ItemGroup` name: exported `HuxleyTableDepot.GetQuestGroup` returns three arrays of quest IDs (`AcquirableItems`, `RewardableItems`, `CompletableItems`) for a group name. It is client-side menu filtering, not evidence for a server inventory-grant path. Enemy drops and quest completion remain the distinct authoritative item sources.
- The 149 NPC groups reference 59 distinct six-digit quest IDs; all 59 resolve in the canonical 530-quest table. This rules out a missing quest definition as the cause of an empty NPC group in the supplied cooked data. It does not prove that every NPC dialog or quest condition works live.
- Every cooked `QuestType_Free` definition has zero objectives. These direct-entry field runs report their outcome through op54 rally telemetry; adding unacquired op55 objective persistence would serve no supplied quest. `QuestType_FreeQuest` is a separate, acquired journal type with objectives and retains its existing op55 path.
- `HuxleySeqAct_MoveItem` calls client `ReQuestAddItem` only through its ADD input (op46) and `ReQuestDelItem` through DELETE (op50). An audit of all 28 US-client city `*Script*.war` packages found 18 MoveItem actions in six maps, all linked through DELETE, involving only training certificate `613994` and train ticket `613995`; none links ADD. The server's narrow city op50 authorization follows those actions. The same sweep found 327 vendor `OpenTradeMenu` actions; four `SP_Nostalonia_Script_C` actions name `Battle_Item_BA` or `SK_BA`, neither present among the 43 cooked ItemGroups. The missing-group actions are wired to Johnny's and Sharon's dialogue button outputs, not orphaned Kismet objects, but runtime dialogue reachability remains untested. Nearby dealers use the distinct, existing `BI_BA` and `SK_Me_BA` groups; those are not evidence that either missing name is an alias.
- A separate `QuestExtractor dumpraw` pass over the Korean `HuxleyItemTable.upk` produced 44 item groups. Its group-name set contains all 43 dev names plus only `SK_Me_T_Old`; it also lacks `Battle_Item_BA` and `SK_BA`. Thus substituting Korean vendor metadata does not restore those two exact names. Keep the Kismet references unresolved until an original table revision or live UI behavior establishes their intended stock.
- A broader US-client cooked-map audit found ADD wiring outside ordinary city scripts: 12 `Maps/CityQuest&Event/{Sapiens,Alternative}/E_Class/City-Quest_*_Pre.war` packages link `HuxleySeqAct_MoveItem` through ADD. Their `HuxleySeqAct_IsEquipItem` False branch grants one missing class equipment item before `HuxleySeqAct_QuestAcquire`. Sapiens actions reference quests `800045/800050/800051` with `213xxx/214xxx/215xxx` gear and `800044/800052/800053` with `110101/110401/110801`; Alternative actions reference `850045/850050/850051` with `216xxx/217xxx/218xxx` gear and `850044/850052/850053` with the same three `110xxx` items. The separate `City-Q_SL_AT_E_Class.war` also contains linked ADD actions. None of the 1,291 `Maps/Quest/*.war` packages contains `HuxleySeqAct_MoveItem`. Crucially, **all twelve quest IDs are absent from the supplied 530-entry dev `HuxleyQuestData` catalog and the Korean quest-table extraction**. At least some referenced gear IDs (for example `213001`) are absent from the dev item dump; Korean item extraction emits UELib deserialization errors and cannot establish a complete item-presence claim. The first attempted test of a narrow grant route exposed the missing dev definitions before runtime. These may be orphaned class-promotion assets from a different content revision. Keep op46 rejection until compatible quest/item definitions and runtime reachability are established; a city/faction allowlist alone would permit invented item minting. This is static Kismet evidence, not a live class-promotion test.
- A separate extraction of `C:\Games\Huxley Korea\huxleygame\Content\DataTable\HuxleyItemTable.upk` produced 44 named item groups, versus 43 in the dev table. The sole Korean-only name is `SK_Me_T_Old`; **neither build contains `Battle_Item_BA` or `SK_BA`**, despite the four linked dev-city trade actions named above. This rules out those menus being repaired by merely loading the Korean table. Do not alias them to neighboring groups without an original mapping or observed client behavior.
- The exported `HuxleySubwayMenu.OnClick` resolves the selected station through `FindCityZoneByAreaName`, then calls `MoveMap(zoneIndex, areaIndex)` without a local unlock or fare check. The server's op20 destination validation therefore follows a visible client path; `HuxleyCityTransportData.UseFee` belongs to the separate tram/transport transaction, not evidence of a subway charge. A hidden original-server unlock policy remains unproven, so no quest gate or subway fee should be invented.
- The city tram is not the subway. A `dumpraw` audit of the dev `City-SP-Nostalonia.war` persistent level found nine `HuxleyCityTransport_NosTram` actors on `Line_1` and eight named `HuxleyTramStopActor` stops (`Line_1_01` through `Line_1_08`). `HuxleyTramStopActor` locally reserves a destination and boards a stopped tram; the MMO controller sends only route index 1 in op104 and no destination in op105. Its op119 leave callback teleports to the server-returned coordinates. The current server charges the cooked route fare but returns its last recorded player position on leave. Map stop coordinates alone cannot identify which stop the client selected; determine whether movement reports advance while riding, or recover an original-server position source, before replacing the leave coordinates with a guessed destination. This needs a controlled tram trace when live testing resumes.
- The exported `HuxleyCityTransport.PlayerGetOn` hard-attaches the pawn to the tram, and `HuxleyMMOPlayerController.PlayerTick` sends changed `Pawn.Location` through op18 every 0.3 seconds while MMO-connected, with no transport exception. The server's op18 path updates the position consumed by op105 leave. An encrypted Game-handler test proves that boarding, a position report, and dismount return the latest reported coordinates and charge one fare. This supports the existing server position source; actual moving-tram and stop timing still require client observation before claiming retail parity.
- The random mission-result menu is dormant in both supplied builds, not an active missing reward path. `HuxleyPCQuestPlayerController.OnReceivedQuestResult` can open `HuxleyRandomRewardMenu`, and its confirm path would submit items through client op48, but `RallyPointFinished` calls that callback only when `ResultDrop=true`. The reproducible `QuestExtractor questresultdata` audit finds zero overrides among all 530 June US-client and 516 April Korean quest records; both class defaults leave `ResultDrop=false`. Native `RequestQuestResult` only stores a delegate and emits no request. Thus the current op62 response branch must not be counted as live quest functionality or extended with guessed grades/rewards for these builds. See `references/quest-native-wire-audit.md` for the script, binary and table evidence.
- All 26 existing ItemGroups referenced by the 28 cooked city `*Script*.war` vendor actions resolve to server item definitions. The 43-group ItemTable contains another 486 distinct item IDs outside that city-vendor union; client op64 carries no NPC/group identity, so purchases are now limited to the union of those 26 city-script groups rather than granting every cooked table item. This still cannot prove which specific vendor menu the player opened. Across all 43 cooked groups, 401 distinct IDs do not resolve to server item definitions; those references are in groups outside the city-vendor union. Two map references (`Battle_Item_BA`, `SK_BA`) have no cooked group at all.
- An exhaustive follow-up decoded `HuxleySeqAct_OpenTradeMenu` exports in all 2,360 `CityQuest&Event` packages whose bytes reference that class. Most are quest/reward-preview actions (1,406 `Trade_Quest`, 1,508 `Trade_QuestRewardPreview`); only 30 explicitly set `Trade_Item` and six `Trade_Weapon`. Default `Trade_Armor` actions were checked separately because they omit `TradeType` in cooked properties. Every nonempty armor/item/weapon group name outside the ordinary city-script audit occurs only in old `City-Quest_*` or `City-Q_SL_CBT2_*` package families, not the `PreActiveCitySubLevel`/`CitySubLevel` names in the supplied 530-quest catalog. Examples are `Item_Battle_PRO`, `SK_T`, `W_T`, `Li_W`, and `W_Dealer`; most of these names are absent from the cooked 43-group ItemTable. No `S-Q-*` package in the scan supplied another named purchasable item group. Therefore this scan does not justify widening the production op64 vendor allowlist to retired item groups; it also does not prove all vendor UI paths live-tested.
- A separate extraction of the April Korean `HuxleyItemTable.upk` (`C:\Games\Huxley Korea\huxleygame\Content\DataTable`) finds 44 `HuxleyItemGroupData` records versus 43 in the dev table. Its only extra group name is `SK_Me_T_Old`; neither `Battle_Item_BA` nor `SK_BA` exists in either table. The two unresolved `SP_Nostalonia_Script_C` references therefore cannot be restored by copying a Korean vendor group, and no guessed purchase group is authorized.
- Current implementation evidence is in `Lunarite.Server/Handlers/{Agent,Game,Battle}.cs`, focused `Network/*Store.cs` and catalog classes, `Packets/Incoming` and `Packets/Outgoing`, and `Lunarite.Server.Tests`. Static tests should be followed by representative client observations for each feature family.
- Native server event 78 (`sub_11118B40`) reads a count followed by three-byte inventory/cell/state entries, mutates the corresponding local item state, then uses the exported `HuxleyMMOSubsystem.OnUpdatedItemState` callback, which only refreshes HUD views. The packet serializer exists, but no server transaction has yet been tied to this batch form. Do not emit it as a generic inventory refresh or count a serializer alone as a completed item-state feature; event 214 is the separate single-item state update.
- Native server event 224 (`OwnerPingTime`, `0x111267E0`) is a P2P owner-ping list. In the appropriate MMO state, it parses a count byte followed by that many `dd` pairs and updates the client's P2P socket; the original master's publication trigger and pair semantics remain unproven. Event 225 (`sub_11113630`) parses `bd` and dispatches `HUXLEYGAME_OnSimulRoomMsg`, so it belongs to the deferred Simulation Center work. Their decompiles are preserved in `analysis/event-224-native.txt` and `analysis/event-225-native.txt`. Neither event should be emitted just to raise server-event coverage.
- Quest wear is not yet exact for the unpatched Type-4 P2P item bridge. `HuxleyQuestGame.ComputePlayerBattleResult` updates only weapons appearing in `GameStats.PlayerStats[i].WeaponStats`, then all equipped armor. Native `SendQuestResult` op60 serializes four slot-indexed weapon durabilities and five armor durabilities, but its separate four `WeaponUseData` records carry weapon category and kill count, not the used slot. The unpatched Type-4 backup displacement makes the received durability values unreliable except for the terminal boots record, from which the server infers one wear delta and applies it to all equipped weapons and armor; this can over-wear an unused weapon. The opt-in native-backup client path now persists exact slot-specific armor and positive weapon durability without increasing values, but a zero weapon result remains ambiguous between unused and broken. Category is insufficient to reconstruct the exact used-slot set when two equipped weapons share a category. This path needs controlled live validation before enabling it on the server.
- Native server event 194 (`ProcClanChoiceVeteran`) is a one-byte veteran election/removal notification, not the response to the exported `SendClanAppointment` officer-grade transaction. The exported clan appointment menu calls only `SendClanAppointmentInfo` and `SendClanAppointment`; the exported scripts contain `MemberVeteran` data and veteran message enums but no reachable client request for choosing a veteran. The current clan store has officer grades but no veteran election authority. Keep event 194 serializer-only until an original-server trigger or live request is established; emitting it during appointment would invent behavior.
- The normal item table has 312 objects of the unspecialized `HuxleyCityItemData` class. None overrides `ItemClass` or `CityItemGroup`; `HuxleyCityItemData.CreateItemFor` returns no usable client item when `ItemClass` is absent. They are materials and other non-use inventory data, not 312 missing op77 consumable implementations. The concrete specialized subclasses retain separate use, duration, license, recipe, or reset transactions.
- `HuxelyCMSTable` contains four craft-skill menu data objects, but this build's exported `HuxleyCraftSkillSetSubMenu.OnConfirmClick` has its `SendSetCraftWithoutResult`, `SendSetCraft`, and callback assignment commented out and returns true without sending. The table alone is not an active server-owned craft-skill acquisition path; ordinary learned crafting recipes use the separate live `HuxleyCraftMenu`/`SendCraft` path. Do not treat missing craft-skill persistence as a server opcode gap unless a different active client caller is found.

## Implementation order from this inventory

1. Finish the **known broken** co-op mission-admission/entry path with fresh server/client packet correlation. The user's repeated observation is that the invited member stays in the city.
2. Validate client-hosted op55 objective and op54/op60 result-report variants against server quest authority using exported script, cooked Kismet and native binary evidence; test one representative flow per mission type.
3. Expand live coverage of already-built economy/social features before calling them complete. Add missing authority only where evidence demonstrates it.
4. Keep world texture/font compatibility as a **client asset** workstream, distinct from gameplay server completeness. Keep ordinary dev-client UI unchanged except the requested Korean item-shop assets.
5. Leave PvP/Simulation Center untouched until the user reopens that work.

### Co-op entry evidence gate

The 2026-09-23 18:28 two-client attempt has a different first failure from the
earlier permission and gateway-mode traces. The guest `Launch_2.log` received the
host's `CHALLENGE`, all `USES` records and `WELCOME LEVEL=FQ-846000`, completed
`LoadMap: 127.0.0.2/FQ-846000?seamless?Name=player2?team=15`, and began blocking
loads of eight streamed sublevels. It finished `Q-COM_PRE_001`, then asserted in
UE3 `UnTemplate.h:581` (`ArrayMax * ElementSize < INT_MAX`) during the loader flush.
The paired host `Launch.log` accepted `HELLO P=1 VER=3 PID=4`, `LOGIN` and package
`HAVE` records, but has no guest `JOIN` or second `PostLogin` before the connection
closed. The server log has one host-ready event and one guest op128/op52 dispatch;
`DispatchQuestMembers` removes that transport before sending. The matching host
mission URL is `FQ-846000?RemoteMasterServer` without `?Master`, so this run
also does not support a duplicate-dispatch or gateway-only JOIN-branch fix.
The 153,545-byte guest minidump does identify the failed operation when
its return addresses are checked against the June dev executable: `0x10901638`
returns from the `UnTemplate.h:581` array allocator assertion, `0x1096F1DA`
is `FArchive` loading an `FString` with a 2-byte element size, `0x109964FD`
is `FNameEntry` loading its name string, and `0x109BADF4` is
`ULinkerLoad::SerializeNameMap`. The captured name length was `0x80000043`
(-2,147,483,581); its absolute value became `ArrayMax=2,147,483,581`, so a
2-byte allocation exceeded `INT_MAX`. This is package name-map deserialization,
not evidence of malformed quest permission, player-state or pawn packets. The
dump does not retain the live linker object, so it does not prove which package
or seek position supplied that four-byte value.
`MinidumpFile` enumerates 75 captured memory ranges in that dump, consisting of
small thread-stack slices (the largest is 30,820 bytes); there is no retained
linker heap range from which to recover the filename or name-map cursor offline.
The next live `SerializeNameMap` hook is therefore an evidence requirement, not
just an optional corroboration. The retained loader path is
`Quest/Common/Q-COM_PRE_001/FQ-846000/FQ-AT_PRE_001_KIS_A.war`, the next requested
sublevel after `Q-COM_PRE_001`. Its 350,227-byte file is SHA-256 identical in the
dev and Korean installs; the file identity alone does not prove the export is valid
for this build or that it caused the assertion. The host's `Launch.log` completed
`AsyncLevelLoadCompletion FQ-AT_PRE_001_KIS_A` and subsequent streamed sublevels
from that same shared install about 30 seconds earlier. The offline map-family
report also records 298 shared export path/classes and no shared logic-payload
differences between the original dev and Korean variants of this package, although
their visual payloads differ. The guest assertion is therefore not evidence that
the Korean file always fails to parse; client state, streaming order and specific
linker position remain open. Offline UELib successfully reads the summaries of
all eight requested sublevels; `FQ-AT_PRE_001_KIS_A.war` reports 377 names at
offset 125, no package compression and no Huxley `UseCrypt` flag. That rules out
a simple missing file, compressed package or encrypted name table, but does not
explain the live guest's invalid length. `Name=player2` in both clients' generic travel URLs
is inherited from their launch URLs, while the host's mission URL separately
contains authoritative `Name=player1` from op42, so this string is not evidence of
a guest PID/name mix-up. This run does not support changing
guest permission, endpoint selection, or pawn-spawn packets as a crash fix. The
captured invalid DWORD `0x80000043` (`43 00 00 80` on disk) is absent from the
name-table span of **all eight** requested sublevel files. Their NameOffsets and
ImportOffsets bound those spans; a read-only byte scan found zero matches in each.
The bytes do occur later in the payloads of `Q-COM_PRE_001`,
`FQ-AT_PRE_001_KIS_A`, and `Q-SP_PRE_001_Others`, so the DWORD alone cannot
identify a package. This narrows the live assertion to another package, a
mispositioned linker read, or transient memory state; it does not establish which.
The follow-up read-only `tools/audit-ue3-name-table.ps1` accepts all three cooked
UE3 header variants in the installed US-client content and scans only the bounded
name-table region. It parsed **5,567/5,567** `.war`/`.upk` files with no skips and
found zero occurrences of `43 00 00 80`; a positive-control `A_Prefab` search
found the expected bytes in the three `FQ-AT_PRE_001_KIS_*` name tables. Thus
the captured DWORD does not occur verbatim in an installed package name-table
span. This strengthens the case for a bad seek/derived value or transient memory
state, but does not establish which; the live linker filename/index is still needed.
The next failing attempt can use the opt-in `tools/frida_trace_array_assert.py <guest-pid>`
to confirm the oversized array and caller stack at the assertion. A read-only
`ULinkerLoad::SerializeNameMap` hook now retains the active linker's filename,
`NameMapIndex`, `NameCount`, and `NameOffset` on the asserting thread. IDA recovered
the June build's linker offsets and hook address `0x109BACE0`; the next crash
should identify the offending package and name entry without changing the array.
The tracer stays attached
when stdin is closed and writes flushed JSONL to `analysis/array-assert-<pid>.jsonl`
until that process exits or the tracer is interrupted. It has been syntax/CLI
checked, but has not yet been exercised against the failing client. Identify that caller/export
before any asset or binary patch.

The first live retest after permission renewal (2026-09-23 18:11:15–18:11:46, server `server-live.log`, host `Launch.log`, guest `Launch_2.log`) moved the failure boundary. Host PID 1 received op42/op55 at 18:11:15, guest PID 4 permission was refreshed at 18:11:25/35/45, and host room-live dispatched guest op128/op52 at 18:11:45. The host then **accepted** `HELLO P=1 VER=3 PID=4`, received `LOGIN`, `ACK PRG=100`, and `JOIN`; the guest received `WELCOME LEVEL=FQ-846000` and completed map `BeginPlay`. There was no `Not permission!` for PID 4. The prior permission failure is resolved for this run, but co-op still fails after UE3 admission: the host logs `MasterServer: Game server JOIN complete` and no second `PostLogin`/player spawn, while the guest remains loading and the host camera loses its pawn. Native JOIN handler `sub_10ADCA80` takes this gateway-only branch when the **hosted world URL** contains `Master`. A further native read of `sub_111966C0`/`sub_11195340` confirms the ordinary branch injects the permitted guest's PID, name, character, team, level and style into the login URL before calling `Login`. `BuildQuestHostMap` is now gateway-mode aware: with the remote gateway disabled (the standard launcher setting), it omits `?Master` to allow that ordinary branch; with the gateway enabled it retains the flag because its gateway-login cast issue remains unresolved. This is statically and headlessly tested but not live-validated. The running server and clients were not restarted after this change.

The current implementation accepts each squad member's client op34 consent, sends host op42 `QuestCreate` and op55 permission records, then waits for authenticated host op199 or gateway `SERVERREADY` before sending the guest op128 peer endpoints plus op52 `QuestJoin`. Host op51 acknowledges installed room data but does **not** dispatch guests. This order is visible in `Game.cs` (`FinalizePendingQuestDecision`, `QuestCreate`, `OnQuestCreateAck`, `ConfirmQuestHostReady`, `DispatchQuestMembers`). The exported `HuxleyMMOSubsystem.OnQuestEnterRequest` makes server op44 a separate op40 request/response; it is not a proven prerequisite for op52 travel (see [QuestEnterRequest-op44.md](research/QuestEnterRequest-op44.md)).

The preserved 2026-09-22 solo server trace shows authenticated host op199 about 30 seconds after op42 on repeated quest runs while `QUEST_GATEWAY_DISABLED=1`. Thus the gateway is not required merely to receive the host-ready boundary; enabling it speculatively is not a justified co-op fix. That trace has no `[QuestTransport]` guest dispatch, so it cannot validate the two-player path.

A later correlated two-client trace at 11:54:12 registered host permissions for quest 846000 and guest PID 4. The host reported room-live at 11:54:42; the server sent guest op128 then op52, and the guest attempted the host connection. At that same second the host client logged permission expiry for **both** 846000 and PID 4, then rejected `HELLO P=1 VER=3 PID=4` with `Not permission!`. Thus the first proven failure in that run was the 30-second native permission lifetime, not absent guest dispatch or a mismatched guest PID/key. The dev binary's `MMOPermission` insertion replaces an existing PID record and construction timestamps it with `GetTickCount()`. The server now refreshes guest op55 records every 10 seconds while the host is loading and renews them once more immediately before guest op128/op52 dispatch. Both timing boundaries are headless-tested, but neither has yet been retested with both clients after a server restart. The final renewal requires the host's Game link to remain connected at dispatch; in the 11:54:42 trace it was connected and sent the room-live notice, so that specific run could have received the renewal. A different run with an already detached host link cannot receive it over that link. If the guest still fails, capture the new first failing boundary; do not presume the permission refresh also repairs later P2P or UE3 connection problems.

The same live Game log records client opcode 196 immediately after each city `LoadMap`, with a count byte of 2 and two 12-byte endpoint pairs. Dev native `sub_111D4260` (`UDPSocketHelper::SendP2PNetworkAddresses`, source `FP2PSocket.cpp:555`) confirms the wire format `bm`, where each record is relay IP/port followed by `MyAddr` IP/port. The first endpoint in each live pair is one of the server's advertised NAT-test UDP listeners; the second carries the client's `127.0.0.1:7777` or `:7778` socket, matching the independently authenticated op155 report. The server now parses and retains this authenticated, transient relay report but does not let it overwrite op155 endpoint authority or synthesize a new peer route. The original master's response to op196 is still unknown. Dev native `sub_111D5750` identifies the live `76 01` as client opcode 118, `Send HolePunch OK to GameServer`, emitted once after `RecvTestACKHolePunchingPacket`. The server now records this authenticated UDP test ACK separately; its arrival after the guest's `Not permission!` proves it cannot by itself mean UE3 admission succeeded. Opcode 141 is in the deliberately deferred PvP sender set.

For the next failed two-client attempt, correlate these exact boundaries in timestamped server/client logs: both op34 answers; host op42/op55 and op51; host op199 or gateway `SERVERREADY`; guest op128 then op52; guest native `START <host>?seamless` and op51. The **first absent boundary** identifies whether the defect is decision completion, host-ready detection, server dispatch, native travel, or P2P admission. Do not change the packet sequence based solely on the city-side symptom.

In the 11:54:42 failure, the host's authenticated Game link also sent client opcode 199 with value `4` after its earlier room-PID notice `846000`; PID 4 was the invited guest. The current handler rejected that second notice because it was not the active room PID. Native `CClientTcpLink::sub_11108B60` serializes an integer as opcode 199, but its one-argument wrapper alone does not prove which gameplay caller produced either live value or that a guest-PID notice grants admission. Keep the second value in the next correlated trace; do not turn it into a guest spawn or success transition until its active caller is identified.

Exported `HuxleySubsystem.BackupQuestGameData` copies each mission controller's PID, encoded level BP and `StockItem` array, then native `UHuxleySubsystem::SendQuestGameData` emits one client opcode 152 per controller as `bbdwbm` (record index, total records, PID, 16-bit encoded BP, item count, then eight-byte ID/quantity pairs). Its vtable send call was not counted by the existing native sender-pattern coverage script. The server now parses and retains these reports only for authenticated members of the same quest instance; they are transient backup evidence, **not** authority to mint inventory or change progression. The original master's restore trigger and settlement policy remain unknown, and no live client has yet been seen sending op152 in the available trace.

Native `CClientTcpLink::sub_11127BC0` formats guest `HELLO P=1 VER=3 PID=<GetPlayerPID()> KEY=<room key>`; exported `HuxleySubsystem.GetPlayerPID()` returns the selected character PID. The host op55 permission record uses that same guest PID and room key, so a character/account PID mix-up is not supported by this evidence. In the archived September 21 host log, the host received `HELLO P=1 VER=3 PID=2`, sent packets to the guest, then repeatedly reported `UDPClientPacketSize Error (TypeSize:12, ReadBytes:40)` from that endpoint. The paired guest log confirms it browsed the host's `127.0.0.3:7777` from its own `-MULTIHOME=127.0.0.2`, then logged `Connection failed`; the host was bound to `-MULTIHOME=127.0.0.3`. Thus that failure was not a self-addressing op52 destination. This is evidence of a later P2P boundary in that historical attempt, not proof that the current build has the same failure: its gateway/dispatch path changed afterward. A new correlated two-client capture is required before altering the transport.

Another archived September 21 run (`Launch_2-backup-2026.09.21-14.41.52.log`, around 13:32:05) records `[MMOPermission] Permission verify timeout! (PID:846000)` immediately before the host accepts the guest's `HELLO P=1` and repeated `ACK PRG=0` messages. `846000` is the quest index used for the separate gateway permission; it is **not** the guest character PID in that HELLO. The timeout therefore cannot establish expiry of the guest's permission and must not be used as a reason to change guest admission timing. The host still accepted a control connection. A fresh run must correlate the actual guest PID's permission registration, HELLO, and subsequent control progress.

The executable's UDP receiver `sub_111D8540` selects a P2P frame type from the **last byte** of a datagram. Type 4 calls the UDP client handler only when the entire datagram is exactly 12 bytes; otherwise it emits that `TypeSize:12` warning. The archived 40-byte packets therefore failed this native framing check, independently of the earlier TCP HELLO acceptance. In `Launch-backup-2026.09.21-14.27.53.log`, the host's P2P socket initialized at `127.0.0.3:7777`, then its separate `TcpNetDriver` reported port 7777 and accepted the guest's `HELLO P=1` plus `ACK PRG=0`. The guest log `Launch_2-backup-2026.09.21-14.41.52.log` shows its P2P socket on `127.0.0.2:7778` and its game client opening the host's TCP connection. Thus a later-process P2P UDP port of 7778 does not itself prove TCP port 7777 is unavailable or that host election by lowest loopback address is a repair. The sender of the rejected UDP datagram and why it was delivered to that P2P socket remain unproven; do not infer a repair from the warning alone.

Further dev-binary audit: the P2P socket's virtual game-data sender `sub_111D6BB0` appends byte `0x02` to a direct UE3 packet, while its ping sender `sub_111D7220` emits exactly 12 bytes ending in `0x04`. The ordinary IP socket's virtual sender `sub_10943F10` sends its buffer unchanged. Thus the rejected 40-byte/type-4 datagrams do **not** match either of the verified P2P sender shapes; they could be ordinary unwrapped game traffic or another send path, but the archived log contains no payload to distinguish those cases. Before changing op128/op52 ordering or adding NAT-success packets, capture the raw datagram and identify which socket/driver emits it. This also means that merely raising the network byte rate cannot correct the framing mismatch.

A fresh read of the supplied dev IDA database confirms the receiver `sub_111D8540` takes
the datagram's final byte as its type and accepts type 4 only at exactly 12 bytes. The
direct sender `sub_111D6BB0` explicitly writes `buf[Size]=2`; the ping sender
`sub_111D7220` explicitly calls `sendto(...,12,...)` with final byte 4. The archived
40-byte/type-4 error therefore cannot be a correctly framed packet from either of
those paths. The archived host also logged `P2PSocketManager::initSocket` choosing
`127.0.0.3:7778` in one later attempt; op52 contains an IP but no port. The
current local-party host election prefers the lowest loopback address, but this is
only a test heuristic: it does not observe either native socket's bound port, and
the earlier trace proves a P2P UDP socket on 7777 can coexist with a TCP listener
on 7777 in the same host process.
Neither observation proves that the latest co-op path succeeds: the live logs have no
new two-client attempt after the host-election/dispatch changes.

`ProcPeerAddrList` (server op128, `sub_11128F80`) first tests whether `m_pFP2PSocket` exists; if absent, it logs an error. Its success path decodes two IP/port pairs, sets socket offset `+264` to 1, and calls `sub_111D5FA0` to queue a hole-probe packet. It does **not** itself establish a proven playable peer route or create the socket. Native callback op127 (`sub_11128480`) is a separate NAT-test path: it decodes endpoint records, calls `sub_111D7070`, and that function can send client op155 (`bdwdw`) with the test result. The server now advertises two functioning UDP test endpoints through op127 after Game connection, accepts an authenticated op155, and uses its reported public/local endpoints in guest op128 and the quest gateway. A new client capture must establish whether op155 actually follows. `OnQuestJoin` (`sub_1110FF00` -> `sub_1119D180`) sets MMO state 4 and issues `START <host>?seamless`. The archived host log contains `ACK PRG=0` from the guest before the 40-byte warnings, so some UE3 control traffic crossed the boundary. A future failed attempt must distinguish missing P2P-socket setup, partial control-channel progress, and later handshake timeout; the 40-byte warning alone is not sufficient to classify it.

The September 22 `Launch_2.log` shows a concrete endpoint mismatch: the prospective
`.3` quest host bound its P2P UDP socket to `127.0.0.3:7778`, while its separate
`TcpNetDriver` listened on port 7777. `Launch.log` shows the other `.2` client
held P2P UDP `127.0.0.2:7777`. The former fallback published UDP port 7777
in guest op128 and the gateway, targeting the wrong P2P port when `.3` hosted;
TCP 7777 being available does not repair that UDP mismatch. Native
`sub_111D7070` sends op155 as `bdwdw` after a successful op127 NAT test, and
`sub_111D4140` identifies those fields as NAT type, public IP/port, and local
IP/port. The server now parses this authenticated report and carries both
ports into op128 and the gateway target. It retains the old 7777 fallback
when no report arrives. Two server-hosted UDP test endpoints are now advertised
through op127; whether the client sends op155 and selects the reported 7778
port remains unverified without a new client trace. Do not hardcode 7778 or
infer a general port from process order.

The dev executable explains why the P2P port varies. `P2PSocketManager::initSocket`
(`sub_111D8AB0`) reads `[URL] Port`, then `UDPSocketHelper::createSocket`
(`sub_111D3860`) starts there and increments the port until socket creation
succeeds. Its binder (`sub_111D1D70`) uses UDP `INADDR_ANY`, not the client's
`-MULTIHOME` address. Two local clients therefore contend for the same UDP
port even when they have distinct loopback IPs; the second can land on 7778.
This rules out deriving a peer's P2P port from its loopback IP, process order,
or the separate UE3 TCP listener. The server needs the actual bound endpoint
from an authenticated client report or a verified equivalent before emitting
op128. It must not silently infer 7777 for a multi-client host.
The op155 report contains both IP addresses as well as both ports. Quest host
travel now uses those reported public/local addresses when present; an explicit
`QUEST_HOST_IP` remains the public-address override. This completes consumption
of a received report, but does not prove that the current client flow sends one.
The UDP endpoint from op155 is not interchangeable with the UE3 listen-server
join address: op128 uses the former, while op52 retains the host Game TCP peer
or explicit `QUEST_HOST_IP`. This matters when same-PC clients use distinct
`-MULTIHOME` loopback addresses but their P2P sockets bind `INADDR_ANY`.
Native xrefs show op155 has two send sites: the immediate result path in
`sub_111D7070` and the asynchronous NAT-update path in `sub_111D5310`. Both are
downstream of op127 `ProcNATTest`; normal login socket initialization alone does
not send op155. The update path refuses to classify NAT with fewer than two
relay/test endpoints (`NumOfMinimumRelayServers=2`). Therefore sending an op127
with one fabricated endpoint cannot establish a usable report. A faithful
implementation needs two functioning UDP test endpoints and the probe/reply
protocol, or another proven way to learn the bound socket port. The native
`sub_111D75B0`/`sub_111D3F60` path identifies the four-byte `04 00 00 99`
probe; two server UDP listeners now answer it with the observed endpoint and
are advertised by op127 after Game connection. This is offline socket-tested,
not yet verified with a retail client or a cooperative quest run.

Further US-client binary evidence narrows the socket-setup branch. Native successful-login
callback `OnLogin` (`0x111257A0`) calls `sub_111D8AB0` after decoding result 0. That
function logs `P2PSocketManager::initSocket`, creates the 328-byte FP2PSocket object,
and stores its pointer at manager offset `+92`; native NAT-test callback
`sub_11128480` also calls it. The manager constructor initializes that pointer to
null. A normally successful login should therefore create the socket before a later
op128 peer list, and no additional unproven server packet should be inserted merely
to create it. This does not establish why an archived 40-byte packet reached the
type-4/12-byte receiver, nor prove the socket survives every reconnect path.

This inventory should be updated when a concrete script/native finding changes ownership or a representative live test changes status. It intentionally avoids a fictional “feature count” denominator: scripts, callbacks, items and maps are not equal units of work.

### Objective path correction

Mission failure does not require a separate master-server failure packet in the supplied
scripts. `HuxleyQuestGame.QuestFail` calls `QuestAbort(2)`; the field override calls
`QuestAbort(1)`. `QuestAbort` invokes the empty `HuxleySubsystem.OnQuestFailed` event,
then sends `SendPlayerQuestResult` and `SendQuestListenRallyPoint` for each controller
with rally/max/complete all zero and the accumulated BP/QT. The server's existing
op54 path retains that non-reward report; only `QUESTCOMPLETE_REWARD` (enum value 2)
can settle a direct-field BP/QT reward. This is script/static evidence, not a live
failed-mission validation. Do not add a guessed failure opcode or reward on state zero.

The client opcode 46 `SendQuestInsertItem` remains routed but does not grant an arbitrary
item. A `QuestExtractor mapevents` census of all 1,291 US-client cooked quest `.war`
packages found no `HuxleySeqAct_MoveItem` actions. The exported action's ADD input is
the only active `ReQuestAddItem` caller; the copy in
`HuxleySeqAct_SetQuestPlayerStart` is commented out. An authenticated quest instance
alone is insufficient evidence to mint a client-selected item. Loot pickup stays on
the separate, grant-backed op47/op48 path. If another build supplies a cooked ADD
action, this authorization must be revisited against that specific map and item.

All 71 exported `QuestType_Free` direct-entry field quests have no quest objective records. Their instance-authenticated op56 enemy/drop reports, op60 weapon-use result, and op54 rally/reward report are relevant; accepting an op55 objective for those quests would invent an objective absent from the cooked data. Keep the acquired `QuestType_FreeQuest` objective path distinct.

`HuxleySeqAct_ChangeObjectiveState.Activated` calls `HuxleyQuestPlayerController.ObjectiveSetting` for all players when `bTOAllPlayer` is true. Its old `MSGateway.SetObjectiveComplete` branch is commented out; `HuxleyQuestMasterServerGateway.SetObjectiveComplete` is also a no-op. The active controller's `SendObjectives` and update callbacks call `MMO().SendQuestListenObjective`, which is client opcode 55 handled by `Game.OnQuestListenObjective`. `MasterServerEventFired` remains a real gateway RPC, but receiving it is **not** by itself an authoritative objective increment. Do not derive quest progress from arbitrary Kismet event names without a separate active script/native call path.

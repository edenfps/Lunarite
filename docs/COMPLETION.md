# 1:1 completion dashboard

This dashboard separates measured wire coverage from estimates of the much larger MMO feature
surface. Update it only from executable decompilation, exported scripts/data, tests, or live retail
client evidence. Static routing or serialization earns partial credit only: a subsystem receives
high completion credit only after native-binary agreement, authoritative script/data agreement,
stateful regression coverage, and representative end-to-end client validation. A playable tutorial
mission is not equivalent to a complete MMO server.

## Current percentages (2026-09-20)

| Measure | Complete | Basis |
|---|---:|---|
| Native client-send opcode coverage | **151/151 active non-PvP = 100% (151/167 retail total)** | Every active non-PvP constant opcode found in native `CClientTcpLink::Send` calls has a top-level dispatch route. Sixteen Simulation Center/PvP senders are intentionally deferred and remain visible in the audit. Run `tools/protocol-coverage.ps1`. This excludes dynamically selected opcodes and server-to-client completeness. |
| Native server-event surface | **197/197 active non-PvP functional = 100% (202/235 retail total)** | Static executable audit of the recovered client receive-handler registry against server serializers and dynamic opcode send sites. Thirty-five Simulation Center/PvP handlers are intentionally deferred; the other three uncovered retail registrations are native `nullsub` handlers. Run `tools/server-event-coverage.ps1`. This is a surface inventory, not a claim that every emitted layout is live-validated. |
| Early solo playable loop | **58%** | Login, roster, camp, first quest availability/acquisition, fence entry, `Q-820100`, armed spawn, kills, native enemy reports, drops, return, journal and rewards have worked live. Objective/rally callbacks and mission BP/QT have strong static/tests evidence but incomplete live coverage. Remaining: broader chain validation, reconnect/failure cases, exact XP/license UI validation, and representative mission types. |
| Requested non-PvP game | **46% estimated** | Weighted full-server audit with the intentionally deferred 8% PvP area removed from the denominator. Structural implementations without representative live validation receive limited credit. |
| Full retail 1:1 server | **42% estimated** | Weighted audit across the entire retail game, including deferred PvP at 0%. This is an evidence-weighted gameplay estimate, not an opcode count or test-pass percentage. |

## Full-server weighted audit

The cooked `HuxleyQuestItemDropGroupData` dump has 82 groups with
`SpecialDropCount`. The current server independently tests each special entry,
then keeps the first successful entries up to that count; this can favor earlier
table rows when multiple entries succeed. Exported `HuxleyQuestItemDropGroupData`
defines the fields but not the original master-server selection or ordering
algorithm. Do not claim exact special-loot odds or replace this with another
unverified weighting rule; ordinary `DropItem` rolls are separate.

The item catalog now classifies `HuxleyMMOItemData_Helmet` and
`HuxleyMMOItemData_Upbody` through their exported `HuxleyArmorData` ancestry,
alongside base and derived `HuxleyWeaponData`. Previously only the literal
`MultiParts` name and suffixed weapon names were marked repairable. Acquisition
now gives these armor subclasses 100 durability, and vendor sell pricing uses the
class-derived equipment flag rather than an item-ID prefix. Representative cooked
helmet/upper-body rows and acquisition are regression-tested. Broad live equipment
coverage is still missing, so the completion percentages are unchanged.

The exported normal-item and quest-drop `HuxleyDataTable` objects include `MasterIndex`
offsets. Catalog loading now resolves normal child IDs (for example `10001` to `90110001`)
and the separate Etc drop groups (for example `10002` to `49110002`) under their retail
wire IDs. An isolated regression proves the latter guaranteed group grants the former
item. An audit of all 936 enemy group references and 19 cooked scripted-group IDs finds
no missing item definitions in reachable groups. Seven undefined item IDs remain in
unreferenced Etc groups; their items and reachability are not invented. This is an
offline data/transaction milestone, not live validation, so percentages remain unchanged.
An exhaustive `MasterIndex=` search of the supplied `data/tables/*.txt` finds only
these two tables, so no further table-offset migration is indicated by this export.
The newly resolved `90110001` is an exported `HuxleyCityItemData_ItemColorSpray`, not
merely a display name: an offline transaction test now consumes it from inventory and
persists its full item ID on upper-body armor's `nSpray` field, which the exported
`HuxleyMultiGame.PawnEquipArmorPart` uses to recover the color. Its live presentation
still needs client validation.
The normal table contains 23 color sprays (`90110001`–`90110023`) plus one generic
`HuxleyBattleItemData` (`90110100`); a regression verifies all 24 full IDs. The
generic battle item's actual use is not inferred from its class or icon alone.

The cooked drop table has 82 groups with `SpecialDropItem` entries. In every one,
the number of listed candidates exceeds `SpecialDropCount` (for example, seven
candidates with a cap of one). The current server rolls those rows in table order
and takes the first successes, so ordering can materially change the awarded item.
The client scripts define the rows but do not expose the original master-server
selector. This remains an explicit loot-parity gap; replacing the selector with a
different unverified random rule would not establish retail behavior.

The completion-reward graph audit found no unhandled reward modifier. One Alternatives
quest, `871906`, has a transaction named `None` instead of `PO_1`; its cooked array
still contains Give modifiers for 2,250 BP and 2,750 CR. Exported
`HuxleyQuestBindInfos.BuildRewardInfoFromQuestData` iterates every transaction entry,
so the server must retain that reward. An offline regression locks the exact values.
Reward aggregation now follows each cooked completion-transaction object reference
instead of granting every flattened modifier object under a quest. The canonical dump
has 765 referenced Give modifiers, all present and none outside the completion graph;
the isolated suite guards those counts. This preserves current rewards while making
objective-only or orphaned objects in future data unable to grant completion rewards.

The exported `HuxleyLicenseSkill_Special.ConsumeBattleItem` sends listen-server op116 for
the equipped belt item. This transaction now resolves the host's quest-room wire PID to
its character PID, and permits a remote target only through the authenticated same-instance
participant binding. Squad membership alone no longer authorizes inventory reduction.
An encrypted Game-socket regression verifies host and detached-guest consumption and rejects
a cross-instance replay. Live special-skill validation remains outstanding; percentages unchanged.

Acquired `QuestType_FreeQuest` records with `bUseQuestRoom=false` now accept their
city-side op55 objective callbacks, alongside `QuestType_City`. The September 22 live log
showed rejected initial objective reports for `821604` and `821702` despite the former
having just been acquired; both cooked records are no-room FreeQuests. An encrypted Game-socket
regression covers initial, completed, and lowered absolute counts for `821604`. A lowered
count now revokes reward readiness for these no-room quests. This is static/socket-verified,
not yet live-client retested, and does not change the completion estimates.

Authenticated direct-entry `QuestType_Free` missions now retain per-enemy op56 kill counts and op60 weapon-category totals even when the quest was never acquired into the journal. Both paths require the handler's live-instance authorization; unacquired non-field quests remain rejected. This is statically and regression-tested, not yet live-client validated, and does not change the completion estimates.
Terminal direct-field op54 settlement now writes the once-only reward marker in the quest store and BP/QT credit in the player store through one durable redo-journal transaction. A replayed terminal report cannot pay again, while a newly admitted run clears the marker and can earn its own reward. An encrypted host-link regression covers a detached guest; live client validation remains outstanding, so the completion estimates are unchanged.
Their authenticated op54 rally reports now also retain the native rally position, state, BP/QT, squad BP, spawn count and play time. The store preserves a higher op56 kill count against stale zero rally telemetry, rejects non-field and invalid reports, and resets this per-run state on the next admission. An encrypted Game-socket regression exercises the PLAYING (non-reward) report. Live mission validation remains outstanding.
All 71 direct-entry `QuestType_Free` definitions have no separate cooked completion BP, CR, LP, item, or optional-item reward in either faction table. A regression guards this so run BP/QT from op54 cannot be accidentally combined with invented table rewards.
An encrypted Game-socket regression also submits native fixed-block op60 through the production handler on behalf of a detached same-instance guest, then proves a different-instance replay cannot change that guest's saved count.
The complementary encrypted op56 regression now proves a direct-field enemy request both increments the authenticated host's kill count and emits native op59; clearing its live instance binding prevents a second kill or drop.
Confirmed entry to a new direct `QuestType_Free` instance now clears that quest's previous-run kill and weapon totals for every admitted member in the same entry-item transaction. The next run can record lower absolute counts; explicitly acquired quests retain their journal progress. Offline tests cover both cases. Live replay remains unverified.

| Area | Weight | Estimated area completion | Evidence / principal gaps |
|---|---:|---:|---|
| Wire protocol and dispatcher coverage | 30% | 55% proven | Native executable sender/receiver maps exist; all active non-PvP constant sends are routed and all functional active receive callbacks are emittable. This earns only partial gameplay credit because routing and serializers do not prove transaction semantics, sequencing, reconnect behavior, or live compatibility. Sixteen client sends and thirty server events belonging to deferred PvP remain absent. |
| Account, login, server selection, roster | 8% | 60% proven | Persistent authenticated accounts, handoff tickets, isolated rosters, character creation/style/deletion, and native layouts are covered by binary evidence and tests. Shared-NAT identity, recovery, bans, exact retail policies, and broad live multi-account validation remain. |
| Character, inventory and progression | 12% | 55% proven | Core equipment, inventory, durability, socket skills, level/BP/LP, rank, licenses, timed items, repair, tune-up economics, and persistence exist with substantial native/script/test evidence. Original eligible-option weighting, full class/style/item matrices, failure/reconnect behavior, and representative live UI coverage remain. |
| Quests and PvE instances | 18% | 40% proven | Mission objectives, spawning, scripted variants, bosses and maps are client-authored content, not separate server features. The server-owned integration surface implements retail loot tables, quest rewards and optional choices, objective/result ingestion, inventory/equipment transfer, party consent/entry, host-loss return, and durable settlement; the first solo loop has worked live. Party travel, disconnect/failure cases, both factions, and representative reward/loot UI flows still need live validation. Four referenced map packages are absent from both clients. |
| Economy and city services | 10% | 35% proven | Vendors, bank, mail, crafting, auction, trade, timed consumables, warp, medals, and cash stubs have native/script serializers and state tests. Most have little or no representative live-client coverage; external billing, occupation-driven rates, and many UI/error flows remain. |
| Social systems | 8% | 25% proven | Chat, squads/LFS, friends, blocks, clans, presence, paging, and permissions are structurally implemented and packet-tested. Multi-client live behavior, proximity rules, disconnect/rejoin edge cases, clan-war behavior, and broad UI validation remain. |
| Player-hosted PvP / Simulation Center | 8% | 0% active | Intentionally removed on 2026-09-20 and deferred until every other subsystem is complete. Never inspect or reuse the old PvP implementation, tests, serializers, notes, logs, or conclusions. The eventual replacement must begin from a blank branch using fresh primary evidence only. |
| Persistence, administration and deployment | 6% | 30% proven | JSON persistence, atomic per-file generations, backup recovery, durable redo-journaled cross-store transactions, guarded character teardown, and scheduled local snapshots work in tests. Off-host backup replication, monitoring, migrations across releases, security hardening, and packaged deployment remain. |

Character PIDs now use a durable high-water mark instead of `max(current roster)+1`.
Both recoverable JSON generations receive the reservation before character creation or
deletion changes the roster, so retiring the highest PID cannot assign its inventory,
mail, or social references to a later character. Offline tests cover both creation
paths, delete/recreate, and recovery from a damaged primary generation. This is a
persistence safeguard, not new live gameplay coverage; percentages are unchanged.

Scheduled state snapshots now verify every manifest file's length and SHA-256 after publication.
Retention counts only verified snapshots, so one damaged directory cannot displace an older
recoverable generation; damaged and unrelated operator directories remain untouched for
inspection. Offline regressions cover a valid snapshot, tampering, and retention. This is
backup-integrity work, not off-host replication or a client-validated gameplay feature,
so the completion estimates remain unchanged.

Set `HUXLEY_LOG_DIR` before starting the server to keep a separate UTF-8 log file per run.
It captures info, warnings and errors alongside console output, while raw debug packet
payloads remain debugger-only. This opt-in evidence path helps correlate unattended client
tests with server authority; it does not change gameplay coverage or the estimates.

Verified snapshots can now be staged into a new, disjoint recovery directory with
`StateBackup.TryStageRestore`. The staged copy is verified again before its directory
is published; existing destinations and damaged sources are refused. This is an offline
recovery preparation tool, not an automatic cutover or off-host backup. It does not
change the completion estimates.
Run the server executable with `--stage-restore <snapshot-directory> <new-directory>`
to stage a copy without binding any game ports. Inspect the staged copy and stop the
server before any manual cutover; this command never overwrites live state.

Personal-buff state reads now filter expired buffs without deleting their durable
records. The periodic online sweep remains responsible for the expiration transition
before it sends native op232 time updates, so an intervening roster or quest snapshot
cannot consume that transition. This boundary is regression-tested offline; no live
pub/minigame behavior is claimed and the completion estimates are unchanged.

Latest verified fidelity gates: all 530 exported quest-condition graphs use only implemented node/property classes; startup logs and integration tests fail visibly if an alternate table introduces an unsupported class. Fresh quest availability is regression-tested for all three combat styles in both factions: Sapiens `820100/1/2` and Alternatives `870100/1/2`, with sibling variants excluded. Character selection now uses the shared authoritative PlayerInfo builder, restoring persisted medal totals and active personal buffs instead of the former fabricated `1/2` medals and empty buff list. Native 20-byte durative entitlements persist, prune expired records, and restore through city PlayerInfo, mission op42, and city op231. Op77 activates the 27 exported table-backed duration consumables: three city-warp passes, nine contamination modifiers, eight weapon-slot products, and seven temporary licenses. Expiration uses the script-proven `CityItemDuration * 60` seconds; exact overlap rules, contamination replacement behavior, the permanent-license guard, inventory consumption, and client refresh are authoritative. Active group-5 temporary-license targets are merged into every client/mission `LicenseIndexList`, matching `HuxleyLicenseManager`'s sole permission source, and disappear at expiration without altering permanent ownership. The private cash shop exposes all 55 cooked pay items as free packages with persistent purchase, storage, claim, and gifting. An executable-route audit proves 52 use op77, two use the tune-up reset transaction, and one uses the bind-removal transaction; unknown future pay-item classes are reported as unsupported instead of being mistaken for working products. Listener shutdown treats pending-accept disposal as normal and assigns cross-listener client IDs atomically.

The server-event audit now separately reports production runtime references. A serializer proves a
wire layout is available, but no longer implies that a gameplay path constructs it: 187/197 active
non-PvP callbacks currently have a static production reference. The remaining serializer-only
events stay visible by opcode and packet type so missing retail authority can be investigated from
primary evidence instead of inflating feature completion. The deferred set now correctly includes
the five battle callbacks outside the main battle opcode ranges (`OnCityCompulsionReturn`,
`OnBattleInvite`, `OnBattleOccupation`, `BattleZoneRoomState`, and `OnBRTZoneInfo`). Exported script
evidence ties `OnCityCompulsionReturn` exclusively to resetting the Simulation-room UI.
The current ten serializer-only active callbacks are op5 session token, op44 quest-entry request,
op78 item-state batch, op116 server message, op124/125 P2P hole exchange, op194 clan-veteran
notification, op206 P2P relay endpoint, op222 listener notice, and op235 GameGuard state.
This is a routing audit, not proof that all ten need an unconditional server send: each needs a
retail trigger and payload authority before adding a production emission path.

The bank's native two-byte op79/op80 requests now have an encrypted Game-handler regression:
after a table-backed bank expansion, deposit moves the remaining two-item stack from MAIN
to BANK and withdrawal returns it to the chosen MAIN cell, with matching op86/op87 results.
A destination at the expanded capacity boundary is rejected without moving the item.
This verifies the server transaction and wire ordering offline; live bank-menu behavior remains
unverified and the completion estimates are unchanged.

Mail send and item-attachment claim now emit success replies only after the cross-store redo
journal has been published. Recipient arrival notices likewise follow the committed send. This
corrects durability ordering, not live-client mail behavior.

The faction-adjusted vendor sale/undo path is now exercised through an encrypted offline Game
socket: a 128 CR raw sell price at the authoritative 0.5 Sapiens sell rate publishes a 64 CR
undo price, and op177 restores the item while charging exactly 64 CR. This verifies production
packet routing and state mutation, but not live vendor UI behavior.

Authoritative city and direct-field quest rewards now publish native op117 progression events after
their level/BP/LP parameter refresh. The selected player receives the exported level-up message and
effect, nearby players receive the matching city effect, and transitions into levels 10/20/30 also
publish the distinct B/A/S class-promotion event with the native `ELevelClass` value. The transition
is derived under the same progression mutation that grants BP, so retries and rewards that remain
inside one level cannot replay the effect. Static production event coverage consequently rises to
184/197; live visual validation remains outstanding.

Initial combat-style selection now publishes each style-locked starter-weapon swap through native
op217's atomic remove-and-full-item replacement callback. The former remove/op82 sequence rebuilt
weapons from only an ID and fabricated durability 100, temporarily discarding authoritative state,
expiry, tune-up, socket and durability fields until a later full refresh. The replacement is derived
from the same persisted inventory generation as the one-time style mutation, and regression coverage
checks replacement, insertion, removal and unchanged-slot branches. Static production server-event
coverage consequently rises to 185/197.

Agent authentication now preserves the retail login error contract exposed by
`HuxleyPCMMOUIInteraction.OnLogin`: an unknown account returns `LOGIN_WRONGID` (2), while a known
account with invalid credentials returns `LOGIN_WRONGPASSWORD` (5). The previous generic answer 1
hid the actionable client message. Clean-install first-account provisioning remains unchanged.
Account creation follows the adjacent exported `OnNewMember` result contract as well: duplicate ID
returns 3, an empty/invalid ID returns 2, and missing registration credentials retain generic error
1 instead of collapsing every refusal into the same message.
Concurrent authentication now returns the exported `LOGIN_SAMEUSER` result 3 when the account
already owns another Agent link, a central Game link, or a character detached into an active PvE
instance. The intended Agent-to-Game transition remains valid because those two link roles are
tracked independently.

The quest gateway now reconstructs UE3's static-object package map from the server's ordered `USES`
commands and the negotiated generation export ceilings. `MasterServerEventFired` can therefore resolve
its `SequenceEvent` argument to the exact cooked package/export path instead of treating its bits as
an opaque or guessed event. The implementation follows `UPackageMap::Compute`, `IndexToObject`, and
`UActorChannel::ReceivedBunch`; a real `HuxleyGame.u` export-resolution test guards the local package
reader. The same reconstruction now includes generation-capped package name tables, allowing the
gateway to decode `FinishRallyPoint(name TransactionName)` without guessing its FName index.
Mapping resolved event identities into authoritative objective mutations remains incomplete
until representative live mission events are captured and correlated.

Paid-storage claims now journal removal and character-inventory insertion as one durable transfer. A
full inventory restores the storage row in the same generation, and successful delivery updates any
matching possession objective before publication, preventing reconnect-visible loss or quest drift.
New paid and quest-item ownership records now initialize durability from the cooked repairable flag;
ordinary materials and consumables no longer carry a fabricated 100 durability value.
The one-time legacy earned-loot migration now uses the same acquisition constructor, so an old
aggregate-only character also receives cooked durability, pickup binding, and possession timers
when its MAIN inventory is first materialized.

City op11 now bounds its native byte-counted personal-buff and license arrays before writing their
records, so oversized or repaired persistence cannot shift the following inventory stream. Scheduled
state snapshots are retained as the newest 168 generations by default; pruning recognizes only
published directories containing a manifest and therefore leaves operator files and in-progress
snapshots untouched.

Value transfers now use a durable multi-store redo journal. Mail send/claim, two-player trade commit,
auction registration/cancel/bid/timeout return, quest acquisition/sharing/retirement/completion,
license acquire/reset, timed-item activation, battle-record reset, recipe consumption/learning,
crafting with chain-recipe discovery, clan creation, and clan medal contribution stage every affected
mailbox, inventory, progression, auction, quest, license, recipe, durative-item, battle-record and clan generation; they
publish the journal before any store and
replay interrupted commits before static stores load on startup. This closes process-loss windows
that could previously lose or duplicate value between separate JSON commits; the journal path
boundary and exact-once cleanup are regression-tested.
Permanent character deletion uses the same journal across roster, inventory/progression, quest,
transmit, buff, timed-item, recipe, battle-record, friend, block and clan state, preventing a crash
from resurrecting a partially deleted character or leaving PID-owned state behind.
Character creation likewise publishes its roster row and freshly reset progression, inventory and
quest state as one generation, so selection cannot observe a newly created but half-initialized PID.
The one-time combat-style choice and its style-locked starter-weapon replacement are also one
transaction, preventing a crash from persisting a new class while retaining the prior class's guns.
That replacement now retains the complete ownership record when a starter weapon already matches
its target slot; genuinely new style weapons use the cooked acquisition constructor for binding,
durability, and possession-time fields. The native slot-diff packet publishes the persisted result.
Mission pickup batches, individual drops and scripted item insert/reduce events now commit their
inventory mutation with the matching possession-objective counter, so reconnect cannot expose a
quest item and journal counter from different generations.
City buy, sell, sale-undo, drop, bank transfer and medal-donation mutations preserve the same
invariant whenever an acquired possession objective references the affected item.
Equipping or removing weapons, armor, belts and vehicles now recomputes both sides of every
MAIN-slot exchange in that transaction, matching the retail script's MAIN-only `GetItemCount` rule.
Socket-skill transfers and every implemented item-consuming city service now do the same, including
capacity expansion, color spray, QT coupons, personal respawns, timed items, license/tune/bind resets
and recipe learning. Clan medal donations also recompute possession objectives in the same durable
inventory/clan/quest transaction, so donating a required medal cannot leave a stale completed counter.
Auction registration, cancellation, purchase and expired-item return now maintain that invariant as
items enter and leave escrow; the buyer's and returning seller's MAIN totals are authoritative.
Successful auction registration now immediately republishes the seller's fee-adjusted CR balance.
A completed purchase likewise republishes the debited buyer balance and the credited seller balance;
the native sold notification contains only result and item ID, so it cannot update sale proceeds by
itself.
Two-player trade commits synchronize every offered item for both participants, and mail attachment
send/claim synchronizes the sender/recipient respectively, in the same durable transaction.
Successful CR mail sends now republish the sender's debited balance, and CR attachment claims
republish the recipient's credited balance after the durable transaction commits.
City HUD initialization hides its mail-arrival marker, so the server now republishes op205 once per
represented unread mail category on the character's first post-login spawn. Persisted mail received
while offline is visible immediately without replaying duplicate arrival events on later city moves.
Paid city warps now republish the authoritative post-charge CR balance; the exported success callback
only moves the pawn and never applies the client-visible currency deduction itself.
Personal-respawn consumption now republishes `PARAMETER_RESPAWNCOUNT`; client op200 has no response
callback, so without this explicit update the selected-player wrapper retained a stale charge count.
Squad quest-entry item consumption now updates every member's possession objectives in its durable
commit; host-loss restoration recomputes those counters again when the reserved items return.
Login/online item expiry and listen-server item reductions also synchronize MAIN possession counters
atomically. Host-loss restoration now journals the returned entry items and quest counters together.
Accepted party missions now revalidate that every consented character is still online immediately
before room creation. A disconnect in the consent/create gap returns the exported
`RESULTTYPE_QUEST_NOTFOUND_PLAYER` result and aborts before any member's entry items are consumed.
Disconnecting while the party consent prompt is still open now aborts it immediately with that same
retail result instead of leaving the leader and remaining members in a silent timeout window.
Quest-share prompts also close the transaction explicitly: if the recipient disconnects before
answering, the sharer receives the native `NOTFOUND_PLAYER` result instead of waiting silently.
If any accepted member disconnects after entry-item consumption but before the authenticated host
room-live notice, the pending transport is now cancelled by its host-owned reservation. Every
party member's consumed entry items and possession objectives are restored atomically; this closes
the former guest-disconnect path that left the host waiting and the whole party's items stranded.

Runtime quest metadata now retains the exported contamination gate/amount, recommended level and
scaling factor, life limit, room flag and durability parameter instead of discarding them during
catalog compaction. Coverage assertions protect all 177 contamination quests, 528 recommended
levels, 479 nonzero scaling factors and 394 life limits. These values remain client-applied where
the exported scripts make the listen instance authoritative; the server no longer needs fabricated
defaults when a later mission rule consumes them.

The quest extractor now distinguishes the outer `array<QuestTransaction> Transactions` from each
native struct's inner `array<HuxleyUserPropertyModifier> Transactions`. The regenerated canonical
dump contains zero decompiler placeholders and all 454 staged reward arrays. Of those, 453 are tied
to the primary `PO_1` completion stage and one is explicitly `None`; this independently confirms
that aggregating the exported modifier objects at completion does not accidentally grant a
secondary-objective reward.
The runtime reward loader now prefers that quest-aware canonical dump over the older general table
export, which still contains 451 failed transaction decodes. Its audit also requires zero extraction
errors, preventing a future path-order regression from silently restoring incomplete reward data.

Archived retail documentation fixes clan creation at 20,000 CR. The authoritative create transaction
now returns native result 2 when the balance is insufficient, charges only after name/membership
validation succeeds, persists the deduction, and immediately refreshes the client's money parameter.
The separate minimum-rank requirement remains evidence-gated because no preserved source states its
threshold.

Clan emblem changes now follow `HuxleyClanMarkMenu` and its shipped config rather than accepting any
integer. Masters and submasters may select the 64 common marks or their faction's 16 marks; ordinary
officers, unknown IDs, and cross-faction marks receive native invalid-mark result 32.

Clan medal contribution now preserves the exported failure contract: wrong medal IDs return 42,
negative/empty counts return 43, and insufficient owned medals return 44. Each rejection leaves both
inventory and contribution totals unchanged.

Native op60 terminal quest telemetry now decodes and persists all four packed weapon-use records
(`byte category + ushort kills`) instead of discarding the 12-byte block. Values are absolute and
monotonic across duplicate/stale reports. The block lacks enemy identity, so the twelve exported
enemy-plus-weapon objectives remain governed by their authoritative op55 objective reports rather
than an inferred aggregate update.

Quest/system mail unique IDs are now authoritative and idempotent. The first delivery returns
`TRANSMITRET_OK` and emits the arrival event; a replay returns the exported
`TRANSMITRET_DUPLICATE` result without inserting a second message or retriggering the notification.

Personal-vehicle summon now implements the retail ride/dismount toggle for observers. Server op175
announces the equipped vehicle item when mounting and index zero when dismounting; the former path
re-announced the item on both transitions and left other city clients rendering a stale vehicle.

Pub/drink minigame rewards now load all buff and setter fields from the exported retail personal-buff
table instead of a copied server array. This preserves omitted Unreal defaults (notably setter 1's
zero great-success ratio), cumulative flags, modifier types, maximum levels, and durations.

Looking-for-squad invitation consent now resolves the squad key sent in op114 and echoed by native
`SendLFSInviteAnswer`. The previous implementation treated that dword as the leader PID, so a valid
acceptance could never match its pending invitation or add the player to the squad.

Squad creation now enforces the exported `SQUAD_OPTION_CLOSED` (`0x1`) behavior selected by the
client's Private checkbox. Closed squads reject unsolicited directory join applications while the
explicit LFS invitation/consent route remains available.

The invitation route now supports its complete exported use: friend, clan-member, nearby-player,
and LFS menus all converge on `SendLFSInvite`. Invitations therefore target any eligible online
player instead of incorrectly requiring LFS-board registration. Capacity, existing membership,
self-invites, offline targets, block lists, and duplicate squad membership are enforced server-side.
Immediate invitation failures now return native op115 to the inviter; offline, invalid, full-squad
and already-grouped targets no longer produce silence or receive their own failure prompt. A target
disconnect in the invitation race also removes the pending consent and returns the same failure.
Squad directory applications likewise close the leader-disconnect delivery race: an undeliverable
application is removed and the applicant receives native op102 failure instead of waiting forever.
Quest-consent and squad-state notifications are likewise relayed only between authoritative members
of the same squad rather than trusting an arbitrary client-supplied target PID.
Squad creation also emits the localized retail failure results for empty names (`18`), special
characters (`19`), existing membership (`8`), and duplicate squad names (`5`) instead of collapsing
all validation failures into the generic squad error.

LFS directory and player-detail entries now follow the decompiled op110/op113 `dsbbb` field order:
PID, name, current level, level class, and combat style. The previous serializer substituted faction
for level and displaced combat style, corrupting the LFS row and detail popup despite a valid length.
Level class now uses the exported retail thresholds (C 1-9, B 10-19, A 20-29, S 30+) instead of the
former fabricated five-level buckets.

Clan member rows now expose authoritative presence instead of hardcoded `Off`/map zero values.
Offline members use exported state 0, online city members state 2, and active quest members state 4;
the row also carries the persisted/current city map byte consumed by the native clan roster.
Clan invitations also enforce the immutable Sapiens/Alternatives boundary and return the exported
`CLAN_INVALIDTRIBE` result 52 instead of permitting cross-faction membership. Offline targets return
the exported no-member result 10 rather than creating an undeliverable in-memory invitation.

Native client op166 `SendClanNoticeInfo` now reaches the clan-info response used when chat initializes.
The former compatibility branch consumed the exact empty request without responding, and the otherwise
unreachable implementation incorrectly expected a byte that the decompiled sender never transmits.

Native op181 clan-member entries now follow the decompiled field assignments, not merely the correct
`dsbbbbbbdbbd` primitive signature: level, TypeIndex-shaped tribe, style, grade, login, byte map state,
dword map index, veteran flag, contribution rank, and medal contribution. The former semantic order
started with grade and encoded map state as a dword, shifting otherwise length-compatible roster data.

Character-selection roster entries now populate the native five-slot `GenericItem[5]` armor preview
from each character's persisted equipped inventory. The former 160-byte zero placeholder discarded
all saved helmet/body/legs/gloves/boots state and prevented the selection model from matching the
authoritative loadout until after entering the city.

Online timed-item expiry now clears equipped armor from nearby player models as well as the owner's
inventory. It publishes the same native op68/op121 removal pair used by manual armor takeoff, so city
and cooperative-quest peers cannot retain a visually equipped item after authoritative expiry.
An expiring equipped personal vehicle also clears the server's summoned toggle and publishes the
native op175 zero-item dismount update, preventing observers from retaining a vehicle that the owner
no longer possesses.

City visibility restoration now uses the same complete peer snapshot as initial spawn: native op20's
compact preview followed by full op80 records for every equipped armor part. Returning from an
invisible state therefore preserves sockets, spray, durability, tune-up and bind state instead of
temporarily reverting the observed pawn to a partial/default loadout.

The complete peer snapshot now also follows op20 with native op215 clan headline state. Op20 carries
the clan name but has no clan-grade field; publishing the separate authoritative update ensures a
newly visible master, officer or ordinary member is styled with the correct clan rank immediately.

Self and nearby-player records now emit the exported `USERSTATUS_NORMAL` value `1`. Both builders
previously hardcoded zero despite `F_Player_` initializing ordinary characters to status one, which
made the authoritative identity records disagree with the cooked player-wrapper state.

Quest-room creation now completes the retail `op42 Create -> op51 CreateAck -> op53 CanStart` handshake. The binary-proven `db` readiness callback enables room controls and triggers automatic `QuestStart` for missions whose data disables the room UI, rather than depending on stale HUD state.
Joining squad members use op51's first dword as their MMO PID after op52, while the op42 host echoes
the room identity. Both forms now resolve through the connection's authoritative active quest before
op53 is returned; the former host-only comparison discarded every member acknowledgment and left an
accepted member standing in the city.

Successful field enemy reports and quest-item insertion transactions now advance persistent objectives
using the exact exported `BattleZoneObjective` and `ExtraParam` target. This covers all 104
`FQOType_KillEnemy` and 154 `FQOType_PickItem` definitions, caps each counter at its exported
completion count, and marks a quest reward-ready when all primary objectives complete. Later op55
listen-server reports remain authoritative replacements, so the fallback cannot compound a client
counter that is subsequently synchronized.

Field pickup objectives also initialize on normal and shared quest acquisition from the character's
existing `INVEN_MAIN` item totals. This mirrors `HuxleySubsystem.ResetFreeQuestObjectives`, including
counting quest-carried items granted by the acquisition transaction and capping at the exported target,
instead of incorrectly showing zero until a later pickup event.

Subsequent quest insert/reduce transactions now replace those counters from the current aggregate
`INVEN_MAIN` total, matching the exported `OnInsertItem`, `OnUpdateItemNum`, and `OnItemTransaction`
callbacks. Stack inserts no longer double-count seeded items, and losing a required item revokes
reward readiness until the possession target is restored.

The same absolute synchronization follows the three exported city item transactions that call
`HuxleySubsystem.OnItemTransaction`: vendor buy, vendor sell, and manual item drop. Sale undo also
restores the objective total, so city-side possession objectives cannot drift between transactions.
Crafting synchronizes every consumed material plus the normal/lucky output, while quest completion
and retirement synchronize removed carried items and granted rewards for any other active field quest.

The full canonical quest-table dump now preserves every recoverable typed array instead of leaving
52 extractor placeholders. This independently confirms 21 `QuestItems`, 15 `RemoveItems`, and 16
`EnterRequestItems` entries while retaining all 3,737 exported data objects. The compact runtime
quest dump already carried these values; the full archival dump now agrees with it and can support
future nested-object recovery without silently losing those arrays.

Quest-carried items are now transactional gameplay state rather than catalog-only metadata. Accepting
any of the 21 configured quests grants each `QuestItems` entry and reports native inventory deltas;
an inventory-capacity failure rolls the journal acquisition back with result 30. Completion removes
all 15 configured `RemoveItems` before inserting rewards in the same cloned inventory, allowing a
reward to reuse the freed cell without a false full-inventory rejection. Retirement performs the same
temporary-item cleanup without granting rewards, including shared-quest acquisition paths.

Mission entry now enforces each exported quest's player-count bounds before consent and again before
instance creation. Parties below `MinPlayers` receive native `RESULTTYPE_QUEST_DEFICIENCY` (9), parties
above `MaxPlayers` receive `RESULTTYPE_QUEST_EXCEED` (10), and rejected parties cannot consume entry
items. This activates the four two-player cooperative gates and the complete retail one/two/four-player
capacity matrix instead of allowing every mission to launch at any party size.

Mission entry also enforces the retail equipment gate for every party member before consent and again
before item consumption. The INT client messages and exported enum prove the exact outcomes: no armor
returns 11, no weapon returns 12, and any zero-durability equipped weapon/armor returns 13. This keeps
an invalid squad member from travelling into an unusable pawn state and reports the member-specific
reason through the native preflight list.

Client op23 visibility is now functional instead of log-only. The exported escape, city-warp,
teleport and Kismet visibility paths use `SendVisible(false/true)` around relocation; hidden players
are removed from same-presence peers through native op21, hidden movement is retained without being
broadcast, and reappearance sends a fresh native op20 player/equipment snapshot at the authoritative
position. Repeated hide/show requests are idempotent.

Mail arrival now reaches the actual online recipient through native op205, which drives
`HuxleyPCMMOHUD.Callback_TransmitArrive` and its new-message notifier. Both ordinary attached mail and
system-message delivery use the recipient's selected-character connection. Ordinary mail also honors
the recipient's directional block list and returns exported `TRANSMITRET_BLOCKEDPLAYER` (11) before
moving CR or items.

Player trade now uses the complete recovered op85 outcome contract. Requests aimed at a player who
blocks the requester return action 11 without opening a trade, and atomic commit capacity failures
return action 13 to the full local inventory plus action 14 to the remote peer. Invalid/stale commits
use the ordinary action-3 failure instead of misreporting every failure as both inventories being full.
The native op85 action-9 branch only calls `HuxleySubsystem.OnTradeCompletion`; the exported UI
callback closes the trade window without updating the item wrapper. After a successful atomic
exchange, the server now publishes changed MAIN slots with full item replacements/inserts/removes
and the new CR balance to each participant. The exported menu has seven offer slots (`0..6`), so
both the request boundary and atomic commit reject slot 7 instead of accepting an invisible offer.
The complete trade path now has a two-connection encrypted Game-handler regression: client op78
request/accept, both item offers, the first and second confirmations, and each participant's
server op85 completion plus exact item/CR settlement packets. This exercises routing and state
mutation beyond the standalone trade-store tests. It is not live client-menu validation.

Clan medal contribution now validates the two configured retail medal IDs (`609001/609101`), consumes
both requested quantities from aggregate and slotted MAIN inventory as one transaction, advances the
persisted clan and contributing-member totals only after that commit, and refreshes every changed
client cell. Native clan-info and member-list responses now expose those totals instead of always
showing zero. The former two-step reduction could consume the first medal and still accept a failed
second reduction.

Persisted clan permission bytes now authorize the five non-PvP actions identified exactly by
`HuxleyClanPermissionMenu`: subleaders can be delegated invite, notice, punish, mark, and appointment;
squad leaders can be delegated invite/notice; sub-squad leaders can be delegated invite. Masters
remain inherently authorized, ordinary members remain denied, delegated punishment/appointment
cannot target an equal-or-higher rank, and malformed non-boolean permission arrays are rejected.

Clan text authority now matches `HuxleyClanInfoEditMenu`: the INT client permits at most 15
characters for a clan name and 60 for its notice. The backend enforces both boundaries instead of
accepting unreachable 20/200-character values that could not be edited or reproduced by the client.

Clan appointment availability now carries remaining rank capacity rather than the number already
assigned. `HuxleyClanAppointmentMenu` explicitly disables a rank when its byte is zero; the former
inverted response disabled every empty officer rank and only enabled it after an impossible prior
appointment. The inspected member is excluded from the occupied count so an incumbent can retain or
reselect their current rank when its clan-wide cap is full.

Native `OnPlayerInfo` recovery proves op11's 20-byte `CharInfo` is `ushort level, byte style, byte rank, byte job, 3-byte alignment, 3 dwords`. The former 32-bit level shifted style into the job slot, restoring every saved combat style as zero and replaying the welcome/style/training onboarding sequence on every login. Exact byte-offset regression coverage now protects the corrected progression, style, and point boundaries.

The same native handler proves op11 ends with seven inventory counts and the complete 32-byte `GenericItem` stream. Character selection now loads equipped armor, weapons, socket skills and bag contents atomically in that snapshot before map travel. The former post-`LoadMap` burst of one op82 delta per item raced pawn initialization, spawned the character visually unequipped, and forced expensive duplicate inventory/equipment UI reconstruction.

Weapon-license extraction now preserves the retail `WeaponGroup` FName arrays and `PermissionLevelClass` enum instead of discarding both. `HuxleyLicenseManager.HaveWeaponLicense` is mirrored exactly: the first owned license group must equal the weapon's group and its C/B/A/S coverage must meet the weapon's equip-level class. Cooked weapon records that omit their class-default group are restored from their concrete `HuxleyWeaponData_*` subclass. The retail `DefaultPossessedLicense` config intentionally contains no weapon licenses, so no speculative starter grants were added.

Weapon equip is no longer a blind MAIN-to-WEAPON swap. Opcode 68 now duplicates the exported client preflight as server authority and returns the native item results for invalid slot, missing item, wrong item type, broken durability, insufficient level, combat-style mismatch and absent weapon license. Context-sensitive nested-array decoding in UELib recovered every retail weapon-class `TuneUpInfo`: category slot 4 is `WeaponTuneup_Level` and its multiplier is `-1`, so the server applies the packed level tune before comparing the player's level while retaining the base equip level for license class coverage, exactly as the scripts do.

Weapon-slot capacity now mirrors `HuxleyPlayerWrapper.IsOpenedWeaponSlot`: slots 0 and 1 are always
open, while slots 2 and 3 require an unexpired `ECIG_OpenWeaponSlot` durative entitlement whose
`OptionValue` equals that slot. The server accepts both retail optional slots when entitled, rejects
forged equips into locked/invalid slots with item result 5, and applies the same rule to equipped-
inventory swaps. The eight exported 7-day/28-day/test products already activate through op77.

Armor and belt equipment now have the same authoritative boundary. Armor op67 resolves each exported `ItemPos` to its exact helmet/upbody/lowbody/gloves/boots cell, rejects broken or wrong-style armor, and applies the recovered armor level-tune nibble. Belt op70 accepts only `HuxleyBattleItemData`, enforces MAIN bounds and checks its equip level. Exported `EquipRank` is enforced against the persisted native rank enum.

Character deletion now preserves the retail clan-master guard. Native result 5 and its localization explicitly require a master to enter the city and entrust leadership before deletion; the server returns that result before scheduling deletion, while ordinary members retain the existing queued-delete flow. This closes the clan-orphan case without inventing succession policy.

Faction economy multipliers now have one authoritative path. IDA recovery of native `ProcPriceRate` at `0x11112D60` proves server op27 parses exactly eight floats and forwards them unchanged to `OnPriceRate`; the client does not calculate them. Exported inventory and tune-up scripts prove faction selection, multiplication order and integer truncation. The same snapshot now feeds op27 plus vendor buy/sell, repair and tune-up material/fee transactions, preventing displayed prices from diverging from server charges. The supplied clients expose only `BasePriceRate=0.38`, `BuyPriceRate=-0.1`, and `SellPriceRate=0.34`; the backend occupation formula remains unavailable, so production defaults stay neutral rather than fabricating faction advantages.

Quest acquisition no longer collapses every refusal into generic error 1 or acknowledges a duplicate race that failed to commit. The handler now preserves exported `ENUM_RESULTTYPE_QUEST` outcomes for missing quest (3), already acquired (21), condition-impossible (22), and already completed (31), while successful acquisition remains native op37 result 0. The client exposes a separate maximum-journal result 24, but neither supplied scripts/config nor surviving public documentation proves the original capacity, so no arbitrary cap is imposed.

Quest availability no longer invents a level lock from `RecommendLevel`. The retail UI consumes that
field only for difficulty labels and colors; actual level requirements are explicit
`HuxleyCondition_GreaterThan` graphs over `HuxleyUserProperty_Level`. Conditionless free missions
therefore remain selectable below their recommendation, while explicit story/mission gates retain
their exact table-backed authority. This also restores the recommendation-32+ free missions despite
the client-proven level-30 cap.

The quest audit now proves semantic reachability in addition to reference integrity and cycle
freedom. Every one of the 530 exported gates must evaluate true for at least one legal level 1-30,
combat-style 1-3 state using only completed prerequisites from its own faction table. This prevents
a present-but-cross-faction prerequisite or an impossible condition value from silently marooning a
retail quest chain.

Quest completion and retirement now preserve the same exported error contract instead of returning generic error 1 for every refusal. Op49 reports not-acquired (23), wrong/incomplete objective (29), full reward inventory (30), or missing optional-reward selection (37); op48 reports not-acquired (23). These values directly index the client quest-message path and make each rejected transaction diagnosable without changing successful reward behavior.

End-of-run random rewards remain deliberately inactive for this client data set. Exported scripts prove that `HuxleyQuestPlayerController` requests opcode 62 only when `HuxleyQuestData.ResultDrop` is true, and that the response opens the rank/item transfer menu. A typed cooked-package audit of all 530 quest objects plus `Default__HuxleyQuestData` found no `ResultDrop`, `DropGrade_*`, or `TimeLimits` override. `QuestCatalog` now preserves those fields and reports the active count, with regression coverage locking it at zero; the server will not invent a master-side grade-to-item table that this build never activates.

Quest return fallback now preserves the exported per-quest exit metadata. Explicit valid client
destinations still win; sentinel or invalid destinations use an explicit quest exit zone/area before
the character's faction training camp. The departure spot beats a cooked exit area with no zone. The parser distinguishes an omitted Unreal default from an
explicit zero by storing absent exit fields as `-1`, covering tutorial zones 10/110 and the
Alternatives `890022` scout, which now returns to the departure spot, without redirecting unrelated missions.

Direct field/scout reward settlement now observes the retail completion state boundary. Rally telemetry in states 0, 1, or 3 remains valid progress traffic but cannot grant BP/QT or consume the per-run settlement guard; only state 2 (`QUESTCOMPLETE_REWARD`) may commit the reward after telemetry and BP validation. This prevents an early PLAYING packet from paying the run and blocking its eventual completion reward.

Successful clan invitation acceptance now broadcasts native op192 with the entering member name to every online clan member, driving the retail clan-entry system message instead of silently changing persistent membership. Native op78's batch item-state update, op194's veteran election/removal notice, op217's atomic remove-and-full-item replacement, and op222's listener/system-notice serializer are also covered; their retail dispatch points remain evidence-gated. Veteran status is a separate member byte, not an officer appointment grade; the supplied client proves the announcement text but contains no election threshold, cadence, term, or tie-break authority, so the server does not infer it from contribution rank. The reproducible server-event total is now 232/235 (98.7%), or 232/232 after excluding the registry's three native no-op handlers.

Quest loot now requires a validated live instance and an exported enemy/group mapping. Journal quests
must remain acquired; direct-entry `QuestType_Free` scout missions instead retain authorization from
their issued instance token, so a mutable entry condition cannot disable drops midway through a valid
run. Empty retail probability rolls remain empty; unknown enemy IDs return an empty native response.
Anonymous UDP actor-channel closes still provide kill telemetry but no longer guess a representative
drop group or fabricate item `604221`, eliminating duplicate and non-retail loot grants.

The same live-instance authority now governs rally, objective, result, spawn, and room-notice reports. Direct-entry
scout progress no longer re-evaluates mutable availability after travel, while a selected-character PID
can no longer bypass the room check to mutate a different acquired quest. Reports must identify the
exact active quest and either its authenticated player PID or its issued room PID.

Cross-client map comparison found only three Korean-client paths absent from the US client, under table-backed `FQ-846301`. Live startup proved they are older orphaned duplicates: their internal package names collide with three non-identical `FQ-846106` packages, and the later client's byte-identical `FQ-846301` parent intentionally omits them. They are preserved outside the live package search path for analysis. Neither client contains `FQ-830005..8`; those four references remain evidence-gated. See `docs/research/KOREAN_CLIENT_RECOVERY.md`.

Elapsed character deletion now removes inventory/progression, quests, mail, licenses, recipes, personal buffs, durative entitlements and battle records through one authoritative lifecycle. It also removes both owned and inbound friend/block references plus ordinary clan membership. Deletion is refused while the PID remains a clan master or owns auction escrow, and the final elapsed-deletion pass rechecks those invariants before teardown. A full integration test seeds every destructible subsystem and proves the cleanup.

The mail teardown now distinguishes the deleted character's own inbox from mail already
delivered to someone else. Outbound messages remain in the recipient's inbox with
their displayed sender name and unclaimed CR/items; the deleted sender PID becomes 0
so a reused PID cannot become the reply target. Offline tests claim both forms of
value exactly once after deletion. This is server-side lifecycle coverage, not a
live mailbox UI retest, so the completion estimates remain unchanged.

Auction expiration now follows the exported `EAUCTION_REMAIN_TIME` contract: listings live for 24 hours and report the native 12-24h, 2-12h, 30m-2h, deadline-30m and expired buckets. Normal searches exclude expired escrow, timeout searches include only expired escrow, expired bids return `AUCTION_TIMEOUT_DELETED`, and op150 returns only timed-out items. Regression coverage proves opening the auction cannot cancel active listings.

Auction registration now follows `HuxleyAuctionItemSellMenu`: `FeeRate=0.2`, applied to the faction-adjusted item price times stack count, followed by the best owned `ECTLG_Auction` discount. The license catalog preserves exported `DiscountAmount`; the 10/15/20% auction talents are authoritative server inputs rather than UI-only hints.
The same menu calls `GetRealPrice` before the 20% fee: registration now first applies the
authoritative faction buy-rate and truncates to an integer, then truncates the listing fee,
then applies and truncates the auction-license discount. A stateful registration/cancellation
test covers the adjusted charge and escrow return; live auction UI remains unverified.

Auction durability validation now matches the native `HuxleyItemSystemHelper.IsRepairable` call sites: damaged repairable equipment is rejected, while non-repairable materials and consumables retain their valid zero durability and can enter escrow. Regression coverage proves both branches and inventory restoration on rejection.

Auction op143 now honors its full native filter request. The item catalog retains exported class name and combat style for all 3,510 item definitions, maps the nine weapon classes, three style-specific armor weights across five body parts, skill, battle and normal categories to `EAUCTION_CATEGORY`, and applies grade plus level/style usability filters. Generic merged weapon duplicates no longer overwrite their specific retail category.

Auction pagination now treats the request page field as exported `ENUM_PAGE`, not as a literal page number. BEGIN, CUR, END, REFRESH, SEARCH, PREV and NEXT resolve against a per-client/per-list cursor, clamp safely, return native `PAGE_CUR`, and expose the zero-based `CurrentPage` expected by `HuxleyAuctionMenu` (`CurrentPage + 1` is displayed).

Mailbox op178/op199 now uses the same exported `ENUM_PAGE` semantics. PAGE_REFRESH retains the current cursor, PREV/NEXT/BEGIN/END/SEARCH clamp consistently, and a genuinely empty inbox reports `TotalPage=0`, matching `HuxleyMsgListMenu`'s explicit zero-page display branch. Native registry recovery corrected the complete server mail callback family to op199–205 (list, content, send, modify, item, CR, arrival); the former shifted op200–206 mapping could not reach the intended retail UI handlers.

Native op199 recovery adds one critical response rule: navigation values are request commands only.
Once the server resolves BEGIN/CUR/END/REFRESH/SEARCH/PREV/NEXT, a populated list response must carry
`PAGE_CUR` (1). The native client parses mail rows only in that branch; echoing `PAGE_REFRESH` (3)
silently preserved the page count while discarding every `ssdddddb` row. The mailbox now uses the
retail eight-row page size and always returns the resolved direction. Live validation covered a
12-message inbox across pages 0 and 1, visible subjects/content, read-state mutation, and clean logs.

Squad op98/op99 and LFS op109/op110 directories now share the proven page resolver. Responses always carry `PAGE_CUR` when entries are present, report zero-based current pages and zero total pages for empty results, retain independent per-client cursors, and respect the native one-byte entry-count ceiling instead of fabricating a single one-based page.

Chat routing now follows the exported `EChatType` send/receive pairs instead of the previous partial switch. Server-only receive/system enums are rejected from clients; squad and clan messages use authoritative membership, city-map and quest-instance scope prevents cross-map leakage, and blocked senders are filtered per recipient. Exact proximity ranges still require native evidence and live multi-client validation.

The reproducible server-event audit now finds 232 of the client's 235 registered receive opcodes emittable, covering every functional handler; the remaining op45/op89/op171 registrations are native no-ops. IDA recovery adds op5's opaque 16-byte session field, op206's `ddw` P2P relay endpoint, and op235's one-byte legacy GameGuard state. Character selection follows authoritative `PlayerInfo` with native op231/op233 full durative-item/personal-buff lists and op230/op232 Unix-time expiration callbacks. Combat-style selection emits native op216 with the persisted five-slot skill permutation, owned licenses, seven inventory counts and complete 32-byte item stream. Chat mirrors the exported five-messages/10-second flood rule and uses parameterless op198 for the native warning. Vendor sale undo uses recovered client op177 and server op195/op196/op197; the former op177 clan-notice route was a protocol collision. Clan membership and rank changes now broadcast native op215 `dsb`; leadership transfer also recognizes the store's actual success result 26 instead of its former unreachable result-27 branch. City medal donation emits op208 parameter updates and exact inventory-cell changes. Bind-reset success now uses exact op214 `bbb` to update the target item's state instead of resending an unrelated full-item callback. Exported `EILT_Equipped` item durations start only on first equip, while `EILT_Possessed` timers start on authoritative purchase/craft/loot/reward acquisition; both persist absolute Unix expiry without restarting transfers. Native op218 `bbd` updates newly started equipped timers. Elapsed timers are pruned both online and during selection; native op219 `ddbb` queues the original item/tune warning and triggers the retail expiration UI after the final record. Auction escrow completion drives exact op168/op169 notifications. Native P2P control serializers cover op123/124/125/127/128 and op206 with proven layouts; squad PvE emits the locally authoritative op128 host endpoint before guest travel, while external endpoint authority remains evidence-gated. Live two-client rendering remains to be validated.

Auction registration now consumes the exported native `byte cell, int sell price, byte bid period`
request layout. The former `byte, byte, int` parser shifted the price bytes and made valid listings
fail or register with corrupted values; the packet boundary and argument order are regression-tested.
Auction search likewise consumes the exported `byte category, int page, bool usable, byte grade,
string search` layout; its former one-byte page parser displaced every following filter field.
Auction escrow eligibility now follows `HuxleyAuctionItemSellMenu`'s low-nibble item-state bind test.
Socket-skill payload fields are no longer mistaken for binding, while both bind-on-equip and
bind-on-pickup states are rejected without losing the item or registration fee.
Executable decompilation of native op161 recovered each auction-entry field as `dddddbsdb`.
Listings now place the one-byte remaining-time bucket before seller name and the packed 32-bit
tune-up value after it; the former serializer hardcoded the time bucket and put time in the tune field.
Native timeout-list op167 instead consumes the shorter `dddddbs` entry. Its serializer now omits
the active-list tune-up/reversion suffix so multiple expired listings remain byte-aligned.

Equipment transactions now separate the native wearer and observer paths. Armor equip replies to the wearer through op67 `b[bb]`; successful equip/takeoff broadcasts op68 `dbd` with the new item id (or zero), and socket-skill changes broadcast op80 `dbM` with the full updated 32-byte armor record. The former op68 wearer reply invoked `OnEquipArmorOther` with an incompatible payload. The corrected event surface is 189/235 (80.4%); the audit now includes compact collection-expression serializers as well as `PacketWriter` calls.

Quest acquisition now returns the dedicated native op37 `bd` callback recovered from `OnQuestAcquire`, including failure replies. The previous op38 journal-list substitute bypassed `Delegate_QuestAcquire`, `ProcessQuestAcquire`, quest UI updates, and free pickup-objective registration; op38 remains reserved for explicit full-journal requests.

Party quest sharing now uses the recovered directional opcode split: client op42 carries the quest index, server op46 reports per-member `ddb` results, and server op47 opens the recipient's `dd` share-acquire menu. The server enforces exported `ShareType`, squad membership, ownership, and recipient state; acceptance remains on the normal op36/op37 acquire transaction used by the supplied scripts.

Cooperative mission entry now follows the recovered host/join sequence. The leader receives op42 and registers each accepted member through op55 using the same 16-byte room key. Client op51 `db` only acknowledges that the leader installed the room data and unlocks the host start path through server op53; it does not prove that the listen socket exists. The server retains the pending transport until the authenticated host emits op199 after loading the mission, then sends each member op52 `bdm` with the host IPv4 address and key. This closes the observed 30-second race where a member consumed the one-shot seamless-travel callback while the host was still loading and remained in the city. Member permission records carry the same native player-state and 32-byte equipped-item stream used by solo op42. Exported `EnterRequestItems` are preflighted for each member with native result 36 and then consumed from every member in one all-or-nothing server transaction before travel. Silent consent prompts expire after 30 seconds (configurable through `QUEST_DECISION_TIMEOUT_SECONDS`), fill unresolved members with retail `ANSWER_REFUSE`, release all pending state, and report op221 to the leader instead of wedging the squad. `QUEST_HOST_IP` can override the observed host address for NAT/LAN testing. Two-client travel after the live-room timing correction remains live-unverified.

Cooperative room creation now keeps quest authority separate from listen-socket ownership. The local
launcher may select instance 1 as host because it owns UDP 7777 even when another member is squad
leader; creation revalidates every distinct member from the accepted consent snapshot instead of
treating that replacement host as the party's sole authority. A member retiring or otherwise losing
eligibility between preflight and room creation rejects the stale party before entry items are
consumed, while a valid host reassignment no longer changes which party state is checked.

Cooperative mission progress now follows the exported listen-server ownership model. `HuxleyQuestGame.QuestAbort` and `HuxleyQuestPlayerController.CheckListenServer` iterate every quest controller and send each controller's `GetMMOPID()` through the host's single MMO link. Objective, rally/result, and terminal durability reports therefore accept a remote PID only when that PID is an authenticated online participant in the reporter's exact quest and instance; the mutation is applied to that participant rather than the host. Direct-field reward idempotency is likewise keyed by instance and participant, and its balance refresh is sent to the participant's account link rather than the reporting host. Cross-quest, stale-instance, offline, and unauthorized PIDs remain rejected.

The native join callback contains only an IPv4 address and always browses `START <ip>?seamless`; it
cannot advertise a non-default port. Live socket inspection proved that two clients on one Windows
machine bind the P2P/game socket wildcard: the first owns UDP 7777 and the second is moved to 7778
despite `-MULTIHOME`. For an all-loopback party the server therefore selects the canonical launcher's
lowest loopback instance as the client-authored mission host. This preserves the squad leader as host
for real remote/LAN parties while ensuring local op52 guests actually reach the process on port 7777.

The archived client log for the reported fence failure identifies the old fault precisely: op40 accepted quest 820100, op41 opened the consent prompt, then the op34 consent reply received the placeholder op52 failure answer `1`, which maps to `QuestMessage[1]` ("There has been an error in the quest task."). The current path no longer emits op52 there: it aggregates consent, sends native op42 to create the host instance, and reserves op52 for a non-host only after the host's authenticated op199 room-live notice.

Quest availability now distinguishes requested op35 from native server-push op36. Acquisition, retirement, combat-style selection and completion rewards recompute all 530 exported condition graphs and push the authoritative list, so newly unlocked follow-up quests appear and ineligible entries disappear without relogging.

Quest-party equipment synchronization now emits the two native player-wrapper mutations recovered from the client: op120 `d + 32-byte GenericItem` for armor/socket updates and op121 `dbb` for armor removal. Socket-skill equip/takeoff therefore updates both the observer visual packet and the remote quest inventory wrapper that fires `Delegate_QuestUpdateSkillInfo`.

Quest listen-server spawn registration is no longer log-only. Client op61 `d` is validated against the requester's authoritative quest-presence group, then answered with native server op61 `db`; valid party PIDs receive success and unknown/cross-instance PIDs receive `RESULTTYPE_QUEST_NOTFOUND_PLAYER`.

Listen-host diagnostics now preserve the client's overloaded opcode contract. Client op62 `db` is treated as diagnostics when its first field is a live PID (quest EntryIndex values are 8xxxxx), relayed to the authoritative same-instance host through server op63, and the host's result is routed back only to the matching same-instance requester. Ordinary quest-result op62 requests retain their existing rank-result path.

Inventory stack deltas now use the client's exact op83/op84 mutation handlers. Partial drops, stacked purchases and partial vendor sales update only the authoritative count through op83 `bbd`; exhausted drops/sales clear the item index through op84 `bbd`. New/non-stack purchases retain the full GenericItem insertion path.

Vendor sale eligibility now follows the exported inventory menu's authoritative `bPermitSell` flag. Cooked item records explicitly carrying `bPermitSell=false` are rejected without mutation, while permitted items with a displayed `SellPrice` of 0 can be removed for 0 CR exactly as the client allows; a positive price is no longer fabricated as an eligibility rule.

Repair now publishes each repaired equipped armor record through native server op80 `OnUpdateArmorOther`. Nearby city clients therefore receive the authoritative durability and socket-bearing 32-byte item immediately instead of retaining a stale pre-repair armor wrapper until the player respawns or changes maps.

City peer creation now follows native op20's compact armor preview with one native op80 full-item
record for every equipped armor part. Newly visible friends therefore receive persisted socket skills,
spray, durability and bind state immediately instead of remaining on default clothing until a later
equipment mutation happens to refresh that part.

Live armor equip now publishes that same full op80 record immediately after native op68 changes the
visible armor model. Equipping previously customized armor therefore preserves its socket skills,
spray, durability, tune-up and bind state on nearby city clients instead of reducing the observer's
copy to an item ID until the next repair or socket mutation.

Craft outcome codes now match the exported client despite their counterintuitive names: ordinary
`MakeItemIndex` production returns `RESULTTYPE_CRAFT_BREAK` (3), lucky `LuckyItemIndex` production
returns `RESULTTYPE_CRAFT_SUCCESS` (0), and either successful branch can trigger the table-backed
chain-recipe roll. Inventory/material/money commits and native result callbacks now agree on which
item the craft UI displays; the 50% lucky and 100% chain fixtures have deterministic regression paths.

Inventory arrangement now closes with native op90 `b`. The IDA-recovered handler maps discriminator `0` to `OnUpdateInventoryViews` and `1` to `OnUpdateEquipViews`; after the authoritative compacted-item stream, arrange emits inventory refresh `0`, causing the exported MMO subsystem to refresh all HUD inventory views.

Squad members now receive native HUD tracking from authoritative player traffic. Regular op18 movement emits squad-only op108 `dbff` with the sender PID, current leader flag and X/Y coordinates; op20/op138 map travel emits squad-only op109 `db` with the destination map index. Membership and leadership are resolved under the squad lock and only online peers receive the events.

Movement state now rejects NaN and Infinity before they can poison authoritative positions or observer replication. Invalid op18 coordinates receive native op23 `fffw`, restoring the client's last accepted XYZ/yaw through exported `OnMoveInputForce`; no invalid position is broadcast or persisted in presence state.

Battle directory population is no longer a fabricated zero. Native op32 axis and op33 battlefield entries now count online reservations for their requested axis and split them by the character's Sapiens/Alternatives faction; cancellation, battle leave and disconnect immediately remove the player from subsequent counts.

Quest exit now honors the exact unsigned-short city-zone and area pair carried by native client op41. Retail transport nodes can therefore return a player to their intended submap/entry point instead of every mission dumping into training-center map 10/110; the ordinary `0xFFFF/0xFFFF` sentinel still resolves to the selected character's faction camp. Client op43 is the distinct native quest-share-consent request (`sharer PID, quest index`) and no longer ejects a consenting player from the mission.

Subway travel now preserves Unreal's default-zero table index for `Nostalonia_A`. The exported first Sapiens world-zone object omits `EntryIndex` because a plain Unreal `int` defaults to zero; treating that omission as `INDEX_NONE` made the server reject the client's valid map-0 request and reload the current Training Camp. Map 0 is now catalogued, persisted, and accepted, while wire value `0xFF` remains the distinct invalid-map sentinel.

An invalid city-travel request now retains the last valid area as well as its zone; the old fallback always loaded area zero, which could move a player within the same city after a rejected gate selection. The fallback still validates the retained zone against the character's faction and uses that faction's training center only if the stored destination is no longer valid.

Concurrent quest presence and UDP gateway transport now use a live instance token (the host PID), not the quest table index. Two unrelated parties running the same quest no longer share pawn replication, local chat, listen-spawn authorization, op63 diagnostic relays, room credentials, sockets, retry workers, packet state, or loot-return account links. Quest progression remains keyed by the retail quest ID. IDA recovery of native `UWorld::NotifyReceivedText` proved the special gateway JOIN bind checks the hosted world URL for `Master`; op42 now injects `?Master` ahead of the client's hardcoded `?RemoteMasterServer`, bypassing the script-level gateway/controller cast that produced `Login failed` after mission entry. Live fence and simultaneous two-host validation remain required.

City travel now resolves against all 36 exported `HuxleyCityZoneData` objects. Native op20/op138 byte values `0xFF` are sign-extended back to the script-level `INDEX_NONE` value `-1`, allowing the default Nostalonia zone and default spawn position to work instead of attempting nonexistent map/position 255. Unknown map indices retain the player's authoritative current map, and quest-return destinations are catalog-validated before travel.

The item extractor now decodes `TuneUpCategories` as the native `TuneUpCategory` struct array. Across 800 unique items carrying tune metadata, 696 expose seven options capped at four, 29 expose seven options capped at five, and 75 special/time-limited items explicitly cap all seven options at zero. Successful tuning selects only a supported option below its exported cap; it can no longer write one of the unused packed nibbles or advance an exhausted category. `HuxleyTuneupMenu.MakeMaterialList` proves the exact economics: lunarites are multiplied only by the faction tune-up rate, while CR is multiplied by `HuxleyLicenseManager.GetProperCityLicenseValue(0)` and the faction rate before integer truncation. The exported TuneupTalent chain supplies 0/10/15/20% discounts, which the authoritative transaction now applies. Retail localization and `HuxleyPartResetTuneupMenu` prove that item 672002 decreases the selected option by one level; the authoritative transaction now preserves its remaining levels instead of clearing the packed nibble. Reset failures now preserve the exported nine-value result enum, including distinct missing-tool, missing-target, untuned-item, empty-option and invalid-item UI messages. The active `Transaction_TuneUp` protocol exposes no failure-roll result: its enum contains success and validation errors, while success returns the selected option. The separate 1,127-row `HuxleyTuneupRecipeData` table belongs to the retired recipe transformation system renamed to Craft; its success, downgrade and break fields do not apply to packed-option tune-up. Original eligible-option weighting remains unavailable and evidence-gated.

City warp now follows `HuxleyMapMenu_City.OnTeleportClick` and `HuxleyPlayerWrapper.HasCityWarpPass`. Normal requests must meet the configured `MinimumTeleportCharge=990` and deduct that client-computed distance price atomically; forged under-minimum prices are rejected. The special `-1` request succeeds without charging only when the character has an active durative item whose `CityItemGroup` is 15, matching the three exported warp-pass products.

Socket-skill op73 no longer trusts the client preflight. It validates native bounds/type and the
exported armor part, level, rank and combat style before mutating inventory, returning the exact
item-result branches for forged or stale requests.

Personal-vehicle op156 now enforces the exported level requirement for all five retail vehicles
and distinguishes missing items from non-vehicle items through the native city-vehicle result enum.
City-item op77 also preserves the retail missing/invalid/level/rank/unusable/already-learned result
branches instead of collapsing every recipe-use refusal to generic error 1.

The item catalog now retains exported `Binds_Equipped`/`Binds_PickedUp` policy. Successful equipment
transactions apply `ITEMSTATE_BIND=0x01` on first equip without destroying the upper bind-reset-count
nibble, persist it, and emit native op214 so the client's equipped record changes immediately.
Vendor purchase, crafting and quest/reward acquisition apply the distinct retail
`ITEMSTATE_BIND_WHEN_PICKUP=0x02` state. Auction, player-trade and mail-attachment escrow reject both
bind forms atomically; mail returns the dedicated `TRANSMITRET_ITEM_BIND` result instead of misreporting
the item as missing. Mail attachment escrow also preserves the exported `ItemSalesCategory` and rejects
`EISC_PCRoom` items with `TRANSMITRET_CANT_ENCLOSE` (13), matching `HuxleyMsgListMenu.CheckPCRoomItem`.
Player trade repeats `HuxleyUserTradeMenu` eligibility on the server at both offer and atomic commit:
bound items, repairable equipment, and `EISC_PCRoom` items cannot be transferred by crafted requests.

Newly purchased and crafted repairable equipment now starts at durability 100. The former zero-value
construction made brand-new weapons and armor immediately fail the authoritative broken-item check;
non-repairable stack items retain their zero durability field.

Op77 now implements the four exported personal-respawn consumables in addition to craft recipes.
Their table-backed `GeneralValue` grants exactly 1, 10, 8, or 5 persistent respawn charges, consumes
one item, refreshes its inventory cell, and returns native use discriminator 3.

Op77 also implements retail battle-record reset item 671001. It refuses an already-empty record with
native item result 45, otherwise consumes one item, deletes the character's persisted battle telemetry,
refreshes the inventory cell, and returns native use discriminator 6.

Personal-respawn consumables now publish native op16 `PARAMETER_RESPAWNCOUNT` after their op77
transaction commits. The binary's `OnUseItem` dispatcher has no respawn-specific state body, while
the exported MMO game updates the selected-player wrapper only through `OnParameter`; persisting the
count without this callback left the city HUD and the next quest snapshot temporarily inconsistent.

All 23 exported armor color sprays now use their retail op77 path. The transaction requires equipped
upbody armor, atomically consumes one spray, persists the spray item ID in `ItemArmor.nSpray`, refreshes
the local armor record through op79's binary-required 32-byte `GenericItem`, and broadcasts the full
updated armor to city and quest peers. The former two-byte success response was truncated: native
`OnUseItem` case 1 unconditionally parses that record before invoking its replacement callback. This also fixes
the `GenericItem` offset-12 tagged union: armor interprets it as `nSpray`, so expiration pruning no longer
mistakes a spray ID for an old Unix timestamp and deletes colored armor.

All 19 exported `ECIG_HPointCoupon` products now atomically consume one item, grant their exact
table `GeneralValue` as QT (the exported enum explicitly labels H point as QT), persist the balance,
and refresh native parameter 15. This is inventory-owned coupon redemption; cash-shop acquisition is
provided by the private server's explicitly free local ProductTransfer catalog.

Cash-shop balance is now persisted at the account boundary rather than fabricated per character.
Native op227 subtype 0 returns result 7 plus the signed 32-bit balance, and bounded atomic adjustment
is available for later purchase/refund transactions. Native ProductTransfer field layouts and the
paid-storage wire are recovered; free buy, paged storage, inventory claim, and character-addressed
gifting persist through the account store. Original retail metadata and prices remain unavailable.

The cooked game data does retain the product-side item catalog even though the external storefront
catalog is gone. `HuxleyItemTable.PayItem` contains exactly 55 distinct item definitions: 19 H-point
coupons, eight contamination modifiers, eight weapon-slot entitlements, seven temporary licenses,
four respawn packs, three warp passes, two tune resets, and one each for inventory expansion, bank
expansion, bind removal, and battle-record reset. `ItemCatalog` now preserves this table membership
directly from each cooked object reference instead of guessing from ID ranges. All 55 effects already
resolve through authoritative item metadata and their established use paths. The unrecovered boundary
is narrower: Webzen's `IBSCategory/IBSPackage/IBSProduct` files supplied package identifiers, category
layout, price sequences, and cash prices. Those cache files are absent from all supplied installs, so
the original retail storefront mapping/prices remain lost. The private server now exposes all 55 cooked
pay items through a local `/ss` ProductTransfer catalog with clearly inferred categories and zero prices,
plus persistent free purchase, paid storage, inventory claim, and character-addressed gifting.

Timed-item expiration is now atomic across both inventory representations. The prior sweep removed
the slotted `GenericItem` but retained its aggregate ownership count, allowing expired goods to remain
visible to later material/reward logic. Login-time and online sweeps now decrement exact stack totals,
publish both stores together, remove the client cell, and queue native op219 expiration notices.

Inventory initialization now keys on the persisted PID record rather than `list.Count == 0`. An
authoritatively empty inventory—after consuming, selling, dropping, mailing, or trading the final
item—therefore stays empty. Previously the next read rebuilt starter gear and legacy aggregate loot,
creating an unlimited item-duplication path. Rejected purchases now also stage fresh inventory and
progression state locally and publish them only on commit; an invalid hidden-cell request can no
longer suppress first-login inventory initialization.

Quest-host `ConnData` is now a fresh cryptographic 128-bit credential for every op42 instance and is
copied unchanged into each matching op55 permission record and gateway HELLO. The former global
`11/22/33/44` probe pattern allowed one concurrent room's one-time permission to authenticate another.
Each host PID now retains that credential in its own gateway session.

Superseded quest-gateway retry workers cancel through a per-session monotonically increasing handshake
generation. Previously, entering another mission closed the old socket pool but its async probe loop
continued, reopened ports, and sent HELLOs using the replacement room's PID/key. Independent host
sessions can now handshake in parallel without sharing generation or authentication state.

Quest host leave, completion, retirement, map return, and disconnect now remove only that host's gateway
session, cancel its generation, close its UDP pool, clear pending acknowledgements, and release its
account connection. Previously an abandoned handshake continued probing for roughly one minute.

Quest completion now reports the persisted per-PID/per-quest `KilledMonsters` result telemetry.
Each gateway's actor-close counter remains diagnostic only and cannot be consumed by another party.

Mission rally telemetry now preserves signed BP. This implements the death penalty emitted by
`HuxleyQuestGame.ProcessPlayerKilled` while enforcing its exact current-level floor: cumulative BP
cannot fall below the previous level threshold plus one (or zero at level 1).

Vendor purchases now honor every exported `ExchangeItems` cost in addition to CR. The UELib
extractor preserves the previously discarded `ExchangeItemInfo` arrays (575 item-table rows, 460
weapon-table rows, and one skill-table row), and the authoritative transaction consumes those MAIN
inventory materials atomically with the purchase. Missing medals return native item result 33
without charging CR or partially consuming a stack; successful purchases refresh every changed
material cell as well as the acquired item.

Socket-skill equip now follows the retail armor topology instead of assuming that every armor part
has two sockets. The extractor preserves all 2,480 editinline `SkillSockets` references; their
Light/Medium/Heavy/All class defaults recover Minus/At/Plus/All shapes. The authoritative check
matches `HuxleyPCPlayerInventoryMenu.CheckSocketSkillSlot`: armor part and socket shape must match,
absent slots return native result 17, occupied slots return 18, and incompatible slots return 19
before inventory mutation. Regression coverage includes zero-slot starter armor and a one-slot
Phantom lowbody fixture. Live UI validation remains outstanding, so the weighted completion
percentage does not change.

Pub-system buff application now prunes elapsed persisted buffs before evaluating the exported
same-buff and same-group rules. An expired high-level drink buff can no longer return
`BUFF_RESULT_EXIST_HIGHLEVEL`/`BUFF_RESULT_MAX_BUFF_LEVEL` or increment stale state instead of adding
the fresh two-minute buff. The same pruning boundary applies to explicit replacement and deletion.
Same-group hierarchy now also follows `HuxleyPlayerWrapper.AddPersonalBuff`: a different weaker or
equal non-normal-group buff returns `BUFF_RESULT_EXIST_HIGHLEVEL` and preserves the active effect,
while a strictly stronger buff replaces it. The former unconditional group replacement could erase
a stronger paid pub effect with a weaker drink.

Quest difficulty and scout entry are now statically closed against both native code and exported
data. Client op39 carries difficulty `0..2`; server op42 returns that exact byte, and native
`OnQuestCreate` adds `Difficulty=%d` to the pending travel URL before starting the quest world.
`HuxleyQuestGame.InitGame` consumes and clamps the same URL option. Unacquired scout instances are
limited to available `QuestType_Free` records: Gregory Forest's exported scout is quest `820630`,
map `FQ-820630`, unlocked by completion of `820502`. Ordinary `QuestType_FreeQuest` journal quests
remain acquisition-gated. Regression coverage protects the op42 difficulty/minimum-player bytes.

Quest op60 durability persistence now accepts only the native WEAPON/ARMOR buckets and retail
`0..100` percentage range. Displaced or malformed mission item records can no longer overwrite
saved equipment with a byte sourced from another field.

The extracted loot tables now have a regression audit covering all 936 enemy mappings, 589 drop
groups, and 936 enemy-to-group references. Every reference resolves. Retail probabilities above the
nominal 1,000,000 scale remain guaranteed drops, while explicit all-zero padding rows are discarded
instead of being materialized as item zero. Server op59's PID is documented as pickup ownership—the
armed enemy callback supplies the world location—not as the killed enemy identity.

Quest travel retains its empirically required dual-reader Type-4 op42 layout. A standard `GenericItem`
experiment immediately removed all mission weapons, proving that the client needs duplicated
ItemID/Inven/Cell fields for its storage and arData phases. Native recovery now
explains why that compatibility layout is incomplete: P2P op42 type 4 does not construct a
`CQuestGamePlayerBackupInfo`, but cooked `HuxleyQuestGame.ParsePlayerBackupData` consumes its
four-byte item count before decoding the equipment stream. The resulting four-byte displacement
restores duplicated identity fields. Native class lookup now proves `ByteStream2GenericItem`
(`0x111D92B0`) is a bounded raw 32-byte copy. The deployed bridge maps physical `+24` to
displaced `nGeneral1` and uses one ignored terminal record for the final item's displaced
durability. Standard/non-P2P packets emit the exact backup prefix and ordinary 32-byte records.
The serializer, conversion helpers and Korean-client equivalent are recovered in
`analysis/item-travel-layout.md`. A fresh Gregory Forest run live-verified Inferior Acceleration
after the terminal durability record and its header count moved to `INVEN_SKILL` cell 0.
The same op42 player-state block now preserves all four bytes of native `PointInfo.LP`; the former
two-byte write silently truncated mission LP above 65,535 even though city state remained correct.

Quest return no longer replays op11 `PlayerInfo`. That callback is valid only inside a client-
initiated PlayerSelect transaction; live evidence showed it asserting from transaction NONE,
clearing the HUD's quest-menu pointer while leaving the old scene visible, and producing a quest
list whose rows had dead callbacks. Return now preserves the travel-surviving player wrapper,
loads the city, and republishes the final quest journal without corrupting UI ownership.

Inventory/bank expansion items now persist the script-proven steps, consume one table-backed
`GeneralValue=10` item, return use discriminators 4/5, and propagate the steps through city and
quest player-state packets. The recovered capacities—MAIN 48/78 and bank
10/20/30/40—are enforced across equipment/takeoff, skill sockets, vendors, crafting, quest rewards,
mail claims, player trade, legacy materialization and bank transfers; unexpanded characters can no
longer address hidden maximum-capacity cells.
Legacy saves created while the emulator exposed maximum capacities are migrated safely: a MAIN item
in cells 48–77 infers inventory step 2, while the highest occupied bank cell infers steps 2–4. This
preserves existing property rather than making it inaccessible after the corrected bounds activate.

Quest completion reward selection now has an executable faction boundary. Character TypeIndex 1/6
selects `SapiensQuests`, while 11/16 selects `AlternativesQuests`; representative Gregory Forest
follow-up rewards are asserted directly against the exported graphs (500 BP and 500 CR for quests
820502/870502). A lossless audit also locks all 530 quest objects, 765 give-modifiers, 457 populated
species/quest reward entries, and 70 optional groups with 210 positive-quantity choices. This guards
the turn-in path against silently applying the opposite faction's table or dropping exported rewards;
every referenced reward item also resolves through the retail item catalog.
Optional reward turn-in now rejects both the native `INDEX_NONE` value (-1) and out-of-range slots
with result 37 before indexing the choice array. Valid zero-based selections add exactly one exported
choice. The former negative-index path could throw and leave an otherwise completed quest unclaimed.

Vendor authority now preserves the remaining populated retail purchase flags. Item `138401` is
charged against its exported 1,000 QT price (and its 28-medal exchange requirement) rather than CR,
with an op24 QT refresh after success. The 71 exported `bOnlyForSuperUser` records are unavailable to
ordinary characters, and fixed-date item `120681` retains its 2009 deadline and cannot be purchased
after expiration. Regression coverage proves insufficient-QT rollback, exact QT/medal consumption,
unchanged CR, GM rejection, fixed-expiry rejection, and the corresponding catalog metadata.

Vendor authority now also preserves the cooked `bEnabled` flag. Both exported trade menus suppress
disabled definitions, but client op73 can still name an arbitrary item directly; the server now
returns native `RESULTTYPE_ITEM_INVALID_ITEMID` for those requests. This closes access to 145 unique
disabled items represented by 290 cooked object records without affecting definitions that inherit
the enabled default.

PC-room-only acquisition is now authoritative as well. `HuxleyInventoryData.CanGetItem` and both
retail vendor menus require a valid PC-room session for `EISC_PCRoom`; forged op73 requests now
return native `RESULTTYPE_ITEM_PCROOM_ITEM` (48) without changing currency or inventory. The private
server intentionally grants no PC-room entitlement until an evidence-backed session source exists.
The same exported `CanEquipItem` rule is enforced by weapon, armor, belt, and socket-skill
transactions, which return item result 48 for a direct request instead of trusting the UI preflight.
`HuxleyTuneupMenu` applies the matching `CanEnchantItem` restriction; authoritative tune-up now
returns its distinct native PC-room result 9 before consuming CR or lunarites.

Quest abort now follows the captured retail client path. The quest HUD sends `SendBRTLeave` op106
rather than ordinary quest-leave op41 when the player presses ABORT MISSION; an active quest
connection now performs the same persisted teardown, destination fallback, city travel, and journal
refresh as a normal return. Op106 outside a quest retains its existing BRT reservation behavior.

Quest metadata now restores Unreal's omitted-property semantics for `EQuestType`. The enum's zero
value is `QuestType_City`, and 85 ordinary city quests omit `QuestType=` from the cooked export
because they retain that default. They are now cataloged as city quests instead of an empty/unknown
type, preserving their normal acquisition, objective, and turn-in behavior.

Quest metadata also preserves the 95 explicit `QuestSub_Cooperative` values and the 435 inherited
`QuestSub_Single` values. The exported scripts consume this subtype only for the quest-list icon;
the room HUD separately gates all squad starts on squad leadership, so subtype is not used to infer
different master-server transport behavior without stronger binary evidence.

Quest-room start acknowledgments now require the requested quest to match the connection's active
quest and a live instance token. Stale HUD callbacks or forged op119 requests receive the ordinary
nonzero quest failure instead of being acknowledged against an unrelated or already-closed room.

Client-hosted PvE instances now have host-exit recovery. If the authenticated quest host disconnects,
returns, aborts, or completes while guests remain, only clients bound to that exact host token are
returned through the proven quest-exit path, restoring their faction city, journal, and connection
state. Unrelated parties running the same quest use different host tokens and are not disturbed.

Squad quest dispatch now performs the native P2P prerequisite before guest travel. Server opcode 128
installs the host's public/private UDP 7777 endpoint in each guest's `FP2PSocket` peer table before
opcode 52 browses to the host. Previously the browse opened an ordinary ephemeral UDP connection
without a registered peer: the host received the guest `HELLO`, but the guest never advanced from
the challenge/loading phase. Native `ProcPeerAddrList` and archived traces both establish this order.

Guest dispatch no longer depends exclusively on client opcode 199, which is not present in the
archived failed-party client logs. The quest gateway's native `SERVERREADY` control message now
commits the entry reservation and dispatches accepted guests as soon as the host has finished loading
the instance; authenticated op199 remains an idempotent fallback. This uses the host's existing
load-complete boundary instead of a timer, and the transport's host/quest identities are revalidated
before any guest receives op52.

Quest-world travel may close the city MMO TCP connection while the client-hosted UE3 listen world
and UDP gateway remain alive. Disconnect cleanup now requires both a live quest-instance binding and
the one-shot travel marker armed immediately before op42/op52; it
retains the pending party transport, gateway, reserved entry items, and squad membership instead of
treating that expected handoff as a crashed host. The detached binding is cleared when the character
next selects into the city; a room that never became ready then fails its waiting members and restores
every reserved entry item in the same durable transaction. Generic city cleanup can therefore no
longer delete the transport before `SERVERREADY` dispatches it, while an unmarked mid-mission crash
still takes the existing host-loss return path.

The same authenticated readiness boundary now covers solo rooms. Solo quests do not allocate a
party transport, so the former party-only lookup could never commit their reserved
`EnterRequestItems`; reconnecting to the city would restore already-spent requirements. Readiness
now validates the host's quest, instance token, and instance-owner binding directly, committing the
reservation without requiring a synthetic party record.

The earlier pre-travel failure window is also closed. If a party host disconnects after squad consent
but before acknowledging room creation, each waiting member receives native quest result 16
(`RESULTTYPE_QUEST_FAIL_LOADINGROOM`); op52 omits its IP/key payload on that failure exactly as the
client parser expects, so the loading transaction ends without attempting a bogus connection.

Quest entry-item consumption is now a pre-travel reservation instead of an irreversible charge.
The authenticated op199 room-live notice commits it; op51 room-data installation alone does not.
Host loss before room-live atomically restores the exported `EnterRequestItems` to every solo/squad
participant (including an offline host) and refreshes online inventories. A second room cannot
overwrite a still-pending reservation.

Mission item transactions are now bound to the authenticated live quest instance. Client op46/op47/
op48 grants and op50 reductions cannot alter persistent inventory from the city, a stale mission, or
another party's instance; valid journal and direct-entry scout missions retain their established
authorization path.
The shared atomic quest-item store also rejects IDs absent from the 3,510-entry retail catalog before
committing any batch, and zero-count op50 requests are no longer accepted as item reductions.

Cooperative loot ownership now follows the two native quest identities. The op42 host receives its
room/quest index as `HuxleyQuestPickup.OwnerID`, while an op52 guest receives its character PID.
`HuxleyQuestPickup.IsOwner()` compares that value with the local controller's MMOPID; stamping every
drop with the host identity made a guest's rolled loot invisible even when travel succeeded.

Off-directory persistence snapshots are enabled by the canonical server launcher (defaulting to
`backups/`) and can be redirected through `-BackupDirectory`/`HUXLEY_BACKUP_DIR`. The server publishes
one snapshot at startup and hourly thereafter, copying every primary/previous JSON generation into a
temporary sibling, flushing a SHA-256 manifest, and atomically renaming the completed directory. A
partial copy is therefore never exposed as a valid snapshot.

Online absolute-duration effects now advance through the native callbacks instead of remaining
client-active until relog. The existing one-second expiry sweep publishes the `S2C_ALIVE` op2 clock
that maintains exported `HuxleySubsystem.m_nServerTime`, then sends op230 and op232: recovered native
op230 removes elapsed `DurativeItem` records and raises their expiration notification, while exported
`OnUpdateListOfPersonalBuffs` removes elapsed buffs and invokes `OnDeletedPersonalBuff`. Server
persistence prunes both stores at the same inclusive timestamp; zero-expiry entitlements remain
permanent. Deterministic boundary tests cover both record types, and the existing serializer test
locks op2's native tick-milliseconds/Unix-seconds `dd` layout.

Squad membership now survives the Game-link recreation caused by an explicit city `LoadMap`.
Character selection, subway/gate movement, return-to-camp, tutorial exit, and quest return arm a
one-shot transition marker; destination `Spawn` clears it, and disconnect cleanup accepts it only
inside a bounded 120-second loading window. Ordinary unmarked or stale disconnects still remove the
member and transfer leadership, so crashed clients do not become permanent squad ghosts.

Weapon style validation now follows `HuxleyWeaponDataDescriptor` instead of treating every weapon's
display `CombatStyle` as an equip lock. Cross-style C/B/A weapons remain usable when the character
owns the required weapon-group license; S-class weapons remain style-locked. The catalog also
restores the cooked `ExclusiveCombatStyle` CDO defaults for Discharger (Enforcer), Sniper (Phantom),
and RocketLauncher (Avenger), including the ProjRegulator override back to None. Regression coverage
proves both an ordinary cross-style Flinger and an exclusive cross-style Discharger.

City travel authority now preserves the faction boundary encoded by the two independent cooked
`SapiensCityZones` and `AlternativesCityZones` tables. Zone records retain their owning table;
direct move requests, persisted resume locations, and quest-exit destinations reject an otherwise
valid map index belonging to the opposite faction and fall back to the character's own training
camp. This prevents a forged or stale destination from loading a valid package with the wrong
faction's city scripts, NPCs, quest tables, and spawn areas.

Auction's `usable` search flag now applies the same authoritative equipment qualifications as the
inventory path: tuned equip level, player rank, combat style, exclusive weapon-style rules, and the
required weapon-group license. The former auction-only approximation checked base level and display
style alone, so it could advertise an item as immediately usable even though equip would reject it.
Ordinary materials and consumables remain visible when their own level/rank gates pass.
Auction text search now also resolves the localized item name displayed by the client. Seller-name
and numeric-ID lookup remain supported, but typing a visible name such as `Rapier` no longer returns
an empty page merely because the server compared only hidden identifiers.

License acquisition now uses the same effective `LicenseIndexList` as the exported
`HuxleyLicenseDataDescriptor`: active term-license grants count for the already-owned and preceding-
license checks. The prior split let a crafted acquisition request buy a second permanent copy while
the client UI correctly treated the temporary grant as owned; temporary grants still never enter
permanent ownership or license-point refunds.
License reset now likewise distinguishes permanent LP purchases from term entitlements. A character
holding only a temporary license receives the exported `LicenseResetResult_NoLicense` response
without consuming item `670001`; resetting permanent licenses refunds their LP while preserving the
active temporary license list.
City-service pricing now also reads this merged effective list. This matches
`HuxleyLicenseManager.UpdateLicenseList`, which derives its `CityTalentDatum` cache from every
entry in `LicenseIndexList`; an active term city talent therefore affects auction, tune-up, and
repair rates without entering permanent ownership.

All 12 cooked `FQOType_Kills_ByWeapon` objectives are covered by the authoritative objective-report
path. Exported `RecordKillInfo` matches the dead enemy EntryIndex and the current weapon's
`WeaponGroup`, then reports the resulting absolute count through op55; the server validates that
count against the exact cooked objective and persists it. Opcode 56 cannot provide a safe fallback
because it carries only enemy EntryIndex, while terminal op60 carries only aggregate weapon-group
counts. Regression coverage exercises every cooked enemy/category pair and deliberately avoids
inventing a correlation that retail never transmits to the master.

Weighted total remains reported as **42%**. Removing deferred PvP's 8% weight from the denominator remains reported as **46%** for the requested non-PvP scope. Percentages move only when combined binary, script/data, regression, and representative live evidence changes.

Personal-vehicle mount state now clears when the Game link disconnects, including an expected city
travel. Exported `HuxleySubsystem.OnLoadMap` and `HuxleyMultiPlayerControllerBase.LoadCityZone`
both call `ClientTravel(..., TRAVEL_Absolute)`; the apparent seamless path in
`HuxleyMMOPlayerController` is commented out. The destination pawn therefore starts unmounted.
Preserving the former server-side toggle caused an observer joining the destination to see a mount
that the rider no longer had, then see the opposite state on the next summon/dismiss update. A
state-removal regression test passes; representative two-client visual validation is still needed.

City area resume reports now validate the reported byte against the character's current cooked
faction zone before persisting it. The client sends `Zone.GetAreaIndex(CurrentArea)` after entering
a primary area volume; an out-of-range or wrong-faction value is not a valid resume point. This
closes a path where a malformed/stale report could make the next `LoadMap` target an invalid area.

Client-authored Kismet mail now follows the active `HuxleySeqAct_SendMessage` caller: it accepts
`TRANSMITTYPE_QUEST` (enum 3), retains the script's NPC sender name, and delivers only to the
selected character or a guest bound to the same authenticated PvE quest instance. A crafted
client op181 cannot impersonate GM/system mail or direct scripted mail to an unrelated character.
The unique-ID replay guard still applies, and tests cover self, same-instance guest, cross-instance,
cross-quest, unauthenticated, and wrong-type cases. Live mission-mail presentation remains untested.

## Next gates

Cooked-map `HuxleySeqAct_SpawnItem` references were audited separately from enemy drops:
68 quest packages contain 491 explicit `GroupIndex` actions across 19 distinct groups. The
exported action calls `HuxleyItemFactory.RequestItem(GroupIndex)`, reaching client op57 and
server op60 rather than the enemy op56/op59 path. Every referenced group resolves in the
extracted retail drop table; a regression now guards those direct scripted pickups. This
is also exercised through the production Game handler and encrypted socket with a guaranteed
retail group: client op57 yields native op60 with item 603056, while an unknown group yields
an empty op60 response. It is not live pickup validation or proof of the original
special-drop selection order.
Op57 now rejects a valid drop-table group unless it is one of the 19 groups referenced by
the supplied client's cooked `HuxleySeqAct_SpawnItem` actions; enemy-only groups cannot be
rolled through an item factory. Authorization runs before the random roll. The encrypted
Game-socket test covers one allowed scripted group and one valid but unreferenced group.

The cooked drop table contains 82 groups with `SpecialDropCount=1` and multiple
`SpecialDropItem` candidates. The current roller tests candidates independently and takes the
first success, so earlier rows can suppress later rows. Seventy-five of those groups have
candidate probabilities totaling at most 1,000,000; seven exceed that scale. The exported
`HuxleyQuestItemDropGroupData` class exposes fields only, not the original master-server
selection routine. Therefore the exact special-drop rule remains unresolved; do not treat the
current order-biased approximation as retail-equivalent or replace it with an unverified lottery.

Acquired, non-room city quests now accept their selected character's op55 Kismet objective
report without requiring a mission-instance token. The live log had rejected quest 821900's
immediate post-acquisition objective, then refused its reward as premature. An encrypted
offline Game-socket regression now exercises op55 success, cross-PID and unacquired rejection,
and op45 completion through the production handlers. That test uses a persisted Sapiens
character and verifies quest 821900 grants its exported 100 BP, rather than only checking
the completion acknowledgement. This is not yet a live-client validation.
The matching Alternatives city quest 871900 now has the same encrypted end-to-end regression:
an Alternative character acquires it, reports its cooked objective over op55, receives op58,
then settles its cooked BP reward through op45. This verifies the opposite faction's city
quest path offline; it does not establish broader live quest-chain coverage.

An encrypted Game-socket regression now completes Sapiens quest 822807 through production
op45 with its cooked three-choice optional reward. Slot 255 returns the native missing-choice
result without completing or granting an item; slot 1 then completes and persists only item
231205. This verifies the gateway transaction and once-only choice boundary offline, not its
live mission or UI flow, so the completion estimates are unchanged.

Binary string scanning 157 cooked city maps found `HuxleySeqAct_MoveItem` in six packages:
Nostalonia D/D01, Eska C/C01, and the Sapiens/Alternatives training-center scripts.
Their exported Kismet links all target DELETE, never ADD, for certificate 613994 and train
ticket 613995. The MMO controller forwards DELETE to native `SendQuestReduceItem` op50
without a quest-room binding. The gateway admits these owned-item reductions only in the
corresponding cooked city zones (0, 9–16, 100, 110–116) when no mission instance is active.
Op46 ADD remains mission-only. Op50 quantity zero also honors the exported
"remove all" contract already supported by the inventory store. Successful op50 now compares
the slotted inventory before and after the transaction and emits the native remove/replacement
callbacks, so the client is not left displaying a certificate or ticket that persistence removed.
Offline socket tests cover both deletions, every removed slot, and the zone/item/instance guard;
table-backed tests assert that every allowed zone belongs to the named city-map family.
The Kismet guards query `IN_MAIN_INVENTORY`, so city-script deletion now removes only MAIN
copies of those items; the zero-quantity form leaves a banked copy intact. Mission op50's
existing all-inventory semantics are unchanged.
Live presentation is untested.

Quest pickup transfers now consume a per-participant, per-instance entitlement created by
the op56/op57 drop reply. Exported `HuxleyQuestPickup.GiveTo` adds collected items to
`StockItem`; `HuxleyQuestRallyPointEndMenu` sends that list through op47/op48. The
server accepts no more than the issued quantity, commits inventory before consuming the
grant, and clears grants on instance exit. Socket regression tests cover single and batch
transfer, replay rejection, and retry after a failed commit. This is an upper bound,
not proof that a particular world pickup was touched: op56 kill authenticity and live
cooperative pickup behavior still require capture/validation.

Normal mail now addresses the typed recipient name. The exported
`HuxleyMsgListMenu.UILabelButton_SendMsg__OnClicked` passes its UI callback
`PlayerIndex` as the first `SendTransmitSend` argument; that value is not a
character PID. Previously, if it matched an existing PID, the server routed
the message to that unrelated character before consulting `ReceiverName`.
Resolution now requires an existing name, case-insensitively. An encrypted
Game-socket regression sends the native op180 packet with a colliding PID,
checks op201 success and the money refresh, and verifies delivery only to the
named character; an unknown name is also rejected. Live client send/receive
validation remains.

Vendor sale undo now records the proceeds returned by the same atomic sale
transaction, rather than recalculating base `SellPrice`. That transaction
already applies the active faction/city sell-rate multiplier and single-
precision integer truncation; recalculation could make undo charge a different
amount. The regression sells at a non-unit Sapiens test rate,
checks the credited 64 CR, then undoes for exactly 64 CR with the original
balance restored. The native undo-list price field remains a 32-bit integer.

Auction bids now validate the category sent by the active
`HuxleyAuctionMenu.SendAuctionBid` call against the listed item's category.
The client passes `LastCategoryID` from its search result; a mismatched or
stale selection returns the native not-found result without transferring CR
or escrow. An offline regression checks rejection and a matching purchase.
Live auction UI behavior remains unverified, so coverage estimates are unchanged.
Auction listing indices also have a durable high-water mark. Previously, clearing
all listings and restarting reset the index to 1, allowing a stale same-category
selection to refer to a different new listing. New registrations reserve their
index before escrow publication; migration records existing indices before a
sale, cancellation, or timeout return removes them. Both recoverable sequence
generations hold the latest reservation. Offline tests cover sale followed by
a higher-index listing and the retained sequence after the sale.

1. Live-capture and validate the now-complete op54/op56 rally and op55/op58 objective transactions, plus op60 result, op41 leave, op43 share consent, and op45 completion.
2. Recover tune-up eligible-option weighting and non-unit faction-rate authority; live-validate repair, tune-up and licenses. The retail result enum proves validation failures but contains no random-failure outcome.
3. Extend the complete client-send manifest into bidirectional server-event coverage and executable serialization tests.
4. Expand quest-chain validation across species/styles and representative mission types.
5. Leave PvP/Simulation Center untouched until it is the final subsystem; then begin a clean-room rewrite without consulting old PvP work.

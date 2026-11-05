# Host drop during a mission or Simulation match

Missions and Simulation Center matches run on one player's game (the listen host). If that player
crashes or disconnects, everyone else loses the game. Today the server sends the other members
back to the city (`ReturnQuestMembersAfterHostExit`); field-mission BP already banked at
checkpoints is kept, but anything since the last checkpoint is lost.

## What the client already provides

| Piece | Packet | Contents |
|---|---|---|
| Periodic backup, missions | op152 `bbdwbm` per player (`SendQuestGameData`) | PID, encoded run Level BP, carried stock items (id, qty) |
| Periodic backup, Simulation | op152 `wwwbm` (`SendBattleGameData`, every `GameDataBackupTime`) | team scores, remaining seconds, backup type, 31-byte player records |
| Restore block | `CQuestGamePlayerBackupInfo` (`sub_11196CD0`) inside op42/op55/op155/op157 | type 0 none; type 1 `wb` level BP + item pairs (`sub_111962F0`); type 2 31-byte battle record (`sub_11193A50`) |
| Mission restore | `HuxleyQuestGame.ParsePlayerBackupData` | restores each player's stock items (and level BP) on the new host |
| Simulation restore | op155 fields TeamScore0/1, RemainTime -> URL `BackupTeamScore0/1`, `BackupRemainTime` (`sub_11131170`) -> `HuxleyBattleGame.RestoreGameData` | team scores and clock |

The server already stores both backups (`_questGameBackups`, `SimulationTransport.BattleGameData`)
and our op155 serializer already has the score/time fields.

## The obstacle

On the stock client the op42 parser for a P2P quest host (Type 4) does not read the backup block.
`QuestCreate.UseNativeP2PBackup` writes it only for a patched client (`QUEST_P2P_BACKUP_PATCH`).
Members' backups (op55) and Simulation hosts (op155/op157) do parse it. So, without a client patch,
a new mission host cannot receive **its own** run state; everyone else's can be restored.

## Solutions without a client patch

### A. Migrate and settle (recommended next step)

When the host drops and members remain:
1. Credit each member's last op152 backup to the account server-side (run Level BP, as a field
   settlement does). Carried pickups are already granted at pickup time.
2. Pick a surviving member as the new host. Send op42 (Type 4, no backup) and op55 permissions
   for the others (backup type 0 or type 1 with zero BP, since BP was settled), then op52 joins.
3. The mission map restarts from its beginning on the new host. Nobody loses earned BP or loot;
   server-tracked field objectives (op55 counts) carry over. Map-scripted mission progress
   (rally points inside the map) restarts.

For Simulation matches: re-host on a surviving member with op155 carrying the last TeamScore0/1
and RemainTime and op157 type-2 records for every player. That path is fully supported by the
stock client, so a Simulation match continues with its score and clock.

Needs: orchestration in `Handlers/Game.cs` (OnDisconnect), and an unattended live test with
`tools/autotest` killing the host mid-run. Nothing else in the protocol is missing.

### B. Server-hosted missions (removes the problem)

Run each mission on a headless UE3 dedicated server we control, as Battle Zones already do
(`Network/BattleInstanceManager.cs`); `QuestJoin` (op52) can point every player at it. A player
crash then only affects that player. This is the larger project noted in the "Dedicated Quest
Server" plan and needs a dedicated-server build of the quest maps to be verified.

## Status

Option A for missions is built (2026-11-02, `World/QuestInstanceManager.HostLoss.cs`): when a
mission host's link closes, the members are sent to the city as before; once all of them have
spawned there (or after 60 s), the server opens the same quest again with one of them as host,
without taking entry items again. Not yet seen in a live game:
`tools/autotest/Invoke-CoopTest.ps1 -KillHost` kills the host mid-mission and waits for the guest
to be back in the mission.

Simulation matches are re-hosted too (2026-11-02, `RemoveSimulationMemberLocked`): when the owner
of a running match leaves, another member hosts it again. op155 carries the last op152 team scores
and remaining time, and each player's op155/op157 permission carries their 31-byte battle record
as backup type 2, so `HuxleyBattleGame` continues the match with its score and clock. Not yet seen
in a live game.

Run BP is kept (2026-11-02, `DisconnectHandler.SettleRunsAfterHostLoss`): before the mission
restarts, every member (the lost host too) is credited the run BP and QT since the last settlement,
as the checkpoint/abort report would have done. The totals come from the stored op54 rally
reports; op152's Level BP is the same decoded value (`GetEncodeInt(ENCODE_LEVELBP, LevelBP)`),
so it is not paid again. Not yet seen in a live game.

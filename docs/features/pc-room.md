# PC-room bonuses and weapons

Retail Huxley gave extra rewards to players connecting from a Korean internet café ("PC-room").
This server has no café, so the feature is reused as an **operator event**: a GM turns it on for
everyone (for example an event weekend) and off again.

## What retail did

| Bonus | Where | Evidence |
|---|---|---|
| +50% BP on mission and PvP results | added to Level BP when results are computed | `HuxleyBattleGame.uc` `PCROOM_BONUS = 0.5`, `PCRoomBonusBP = nLevelBP * PCROOM_BONUS` |
| +50% PCr (QT) on mission and PvP results | added to the result's QT | `HuxleyBattleGame.uc` `PCRoomBonusPCR = PRI.QT * PCROOM_BONUS` |
| Bonus-point ratio 1.0 -> 1.5 | `GetBonusPointRatio` (`fResult += PCROOM_BONUS`) | `HuxleyBattleGame.uc:800` |
| Bonus icons on the result screen | `UIEventButton_BPBonus`, `UIEventButton_PCRBonus` | `HuxleyQuestRallyPointEndMenu.uc:653` |
| 24 PC-room-only weapons (`EISC_PCRoom`) | sold by the normal weapon vendor (group `W_Me_T`), usable only in a PC-room session | `HuxleyInventoryData.uc` `CanEquipItem`/`CanGetItem`/`CanEnchantItem` |

All bonuses are computed by the client from `m_stPlayer.byPCRoomValid`. The server sets that flag
with `ParameterOut` parameter 17 (`PARAMETER_PCROOM`); the client copies it into the data that
travels into missions and battles, so no other packet changes are needed.

PC-room weapons are `Binds_PickedUp`, cannot be sold (`bPermitSell=false`), cannot be traded or
mailed (`CanTradeItem`, `HuxleyMsgListMenu.CheckPCRoomItem`) and cannot be tuned up outside a
PC-room session. Outside a session they cannot be equipped.

## The weapons

No localization file (English or Korean) contains names for them; the client shows the raw tag.
Four tiers (equip level 1/10/20/30), two weapons per combat style, all `Grade_Precious`.

| ID | Type | Style | Equip lv | Damage | Fire interval | Ammo / max | Grade |
|---|---|---|---|---|---|---|---|
| 109101 | ShotGun | Enforcer | 1 | 33 | 1.265 | 8 / 18 | Precious |
| 109201 | Flinger | Enforcer | 1 | 155 | 1.76 | 4 / 12 | Precious |
| 109401 | MachineGun | Avenger | 1 | 22.9 | 0.12 | 40 / 150 | Precious |
| 109501 | RocketLauncher | Avenger | 1 | 141 | 1.265 | 5 / 15 | Precious |
| 109701 | Sniper | Phantom | 1 | 200 | 1.65 | 3 / 8 | Precious |
| 109851 | Regulator | Phantom | 1 | 57 | 1.045 | 8 / 16 | Precious |
| 119101 | ShotGun | Enforcer | 10 | 37 | 1.265 | 8 / 18 | Precious |
| 119201 | Flinger | Enforcer | 10 | 176 | 1.76 | 4 / 12 | Precious |
| 119401 | MachineGun | Avenger | 10 | 25.7 | 0.12 | 40 / 150 | Precious |
| 119501 | RocketLauncher | Avenger | 10 | 160 | 1.265 | 5 / 15 | Precious |
| 119701 | Sniper | Phantom | 10 | 224 | 1.65 | 3 / 8 | Precious |
| 119851 | Regulator | Phantom | 10 | 64 | 1.045 | 8 / 16 | Precious |
| 129101 | ShotGun | Enforcer | 20 | 41 | 1.265 | 8 / 18 | Precious |
| 129201 | Flinger | Enforcer | 20 | 196 | 1.76 | 4 / 12 | Precious |
| 129401 | MachineGun | Avenger | 20 | 28.5 | 0.12 | 40 / 150 | Precious |
| 129501 | RocketLauncher | Avenger | 20 | 178 | 1.265 | 5 / 12 | Precious |
| 129701 | Sniper | Phantom | 20 | 249 | 1.65 | 3 / 8 | Precious |
| 129851 | Regulator | Phantom | 20 | 71 | 1.045 | 8 / 16 | Precious |
| 139101 | ShotGun | Enforcer | 30 | 45 | 1.265 | 8 / 18 | Precious |
| 139201 | Flinger | Enforcer | 30 | 215 | 1.76 | 4 / 12 | Precious |
| 139401 | MachineGun | Avenger | 30 | 31.4 | 0.12 | 40 / 150 | Precious |
| 139501 | RocketLauncher | Avenger | 30 | 196 | 1.265 | 5 / 12 | Precious |
| 139701 | Sniper | Phantom | 30 | 274 | 1.65 | 3 / 8 | Precious |
| 139851 | Regulator | Phantom | 30 | 74 | 1.045 | 8 / 16 | Precious |

## Running an event on this server

- GM chat command: `!pcroom on` / `!pcroom off` (GM accounts: `HUXLEY_GM_LOGINS`).
  The state is saved in the database (`events.json` row),
  so it survives restarts.
- On: every online player gets parameter 17 = 1 and a notice; players logging in get it at
  character selection. The client then applies +50% BP/PCr and shows the PC-room weapons at the
  vendor. The server lifts its `EISC_PCRoom` refusals for buying, equipping and tune-up.
- Off: parameter 17 = 0. Owned PC-room weapons stay in inventories but cannot be equipped,
  bought or tuned until the next event (retail rule). Mail and trade stay blocked at all times.
- Code: `Network/ServerEvents.cs`, `Network/EquipmentRules.cs`, `Network/PlayerStore.cs`
  (`TryBuyItem`, tune-up), `Handlers/Game.cs` (`PcRoomParameter`, `!pcroom`).

Not verified in game yet: the vendor listing and the result-screen bonus icons.

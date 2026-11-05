# Pub drinking minigame (unfinished in the client)

A bar feature where a player buys a drink, plays a gauge-bar timing game, and receives a short
personal buff. The server side and the data exist; the client's menu does not.

## What exists in the client

- **Requests:** `SendBuyDrink(setterID)` (op204, `d`) and `SendGaugeBarGameResult(setterID,
  playSeconds)` (op202, `dd`). Console exec functions call them directly:
  `ExecBuyDrink <setter>` and `ExecPubSystemResult <setter> <seconds>`
  (`HuxleyPCMMOPlayerController.uc:715-727`).
- **Callbacks:** `OnBuyDrink(ENUM_DRINK_RESULT_CODE)`: OK, UNDEFINED_SETTER, NOT_ENOUGH_MONEY. The
  handler only logs. Buff results arrive on the normal personal-buff list and timer packets.
- **Data** (`data/tables/HuxleyPersonalBuffTable.txt`), group `emPersonalBuffGroup_PubSystem`,
  each buff lasts 120 s:

| Buff | Effect | Value | Stacks |
|---|---|---|---|
| 1 | `RageGaugeRatio` (rage gauge fills faster) | +10 | no |
| 2 | `GaugeBarSpeed` (next gauge game is harder: "handicap") | +10 | yes |
| 3 | `DeadDrunk` (side effect) | -10 | no |

  Setters (drinks): `DefaultGaugeSpeed`, `SideEffectProbability` (70 and 50; 100 = 1%), and goal
  windows for Great success, Success, Handicap and Side effect, each with a buff and a maximum
  buff level. Price inherits 0.
- **Animation:** `SC_Drink` exists in every character package.

## What is missing

- `HuxleyPCMMOHUD.OpenPubScene` / `ClosePubScene` are commented out, and the `HuxleyPubMenu` class
  they reference does not exist. No UI scene for the gauge bar exists in any package. No NPC,
  Kismet action or trade type opens it.
- So the game cannot be shown to players. Typing `ExecBuyDrink 1` in the console reaches the
  server, but nothing displays the bar.

## What the server does today

`Handlers/Game.cs` `OnBuyDrink` / `OnGaugeBarGameResult`: validates the setter, charges the price
(0), remembers the pending drink, then resolves the gauge position from `playSeconds x
GaugeSpeed` against the setter's goal windows (the side-effect roll first) and grants the chosen
personal buff. The resolution formula is our reconstruction; retail's is unknown.

## Options for a custom feature later

1. **No client change:** an NPC dialogue or chat command that calls the existing buy/result flow
   server-side (e.g. `!drink`) and grants a buff by chance, without the gauge game.
2. **New UI:** a new UI scene and menu class would be a client content change (a new `.u`/`.upk`),
   which the project currently avoids.
3. The buffs themselves (rage gauge +10%) work in missions and PvP through the normal
   personal-buff system, so either option gives players a real effect.

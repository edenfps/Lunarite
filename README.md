<p align="center"><img src=".github/lunarite.webp" alt="Lunarite" width="140"></p>

<h1 align="center">Lunarite</h1>

A private server for **Huxley: The Dystopia** (Webzen, 2010), the shut-down MMO first-person
shooter, rebuilt by reverse engineering so the original 2010 client plays against it unmodified.
The target is retail behavior: the client's own packet layouts, result codes, data tables and
names, recovered from the client's native code, its UnrealScript and its cooked packages.

## Status

Login, character creation, the city, tutorial, quests and player-hosted co-op missions, loot and
rewards, inventory and equipment, vendors, crafting, repair, mail, trade, auction, squads, friends,
clans, the Simulation Center (PvP) and the battlefield war front are implemented, and co-op and
PvP matches work between players. `docs/COMPLETION.md` tracks coverage per subsystem and

## Requirements

- The .NET 9 SDK, on Windows or Linux (`docs/SETUP.md`).
- The Huxley client, and specifically the **Webzen development build of June 2010**:
  `Binaries\HuxleyMMOGame.exe` compiled 2010-06-21 from the `HuxleyStableRC` branch
  (UE3 EngineVersion 3240, packages 496/23), unmodified SHA-256
  `916C8029AA0A784263749A9C0BF99D83AB9F54363E86EE619439870022611EAB`. Packet layouts, opcodes and
  native addresses in this repository are taken from that executable, so other builds (the April
  2010 Korean retail client, for example) are not supported.
- That build is missing part of the retail `LevelPackages`; add the level packages from the Korean
  client (`docs/research/ASSET_REFERENCE_VALIDATION.md`).
- A copy of that client on the server machine too: the server reads its map list and item names,
  and every battlefield runs as the client's own dedicated-server mode
  (`HuxleyMMOGame.exe HuxleyServer <map>`). Keep spaces out of its path; UE3 splits command-line
  paths at spaces.

## Running the server

```powershell
dotnet run -c Release --project Lunarite.Server\Lunarite.Server.csproj
```

The server finds its data tables in `data/` by searching up from its build folder, and keeps its
state in `huxley.db` (SQLite) next to the built `Lunarite.Server.dll`. Players register accounts from
the client's login screen; on an empty database, the first login also creates its account.

Ports to open for players: TCP 45405 (login), 45406 (game) and 8090 (client patch server); UDP
45407-45408 (NAT test), 56983 (server list ping) and 7788-7887 (battlefield instances, one port each). TCP 55902 (results
from the battlefield instances) and HTTP 80 (cash shop catalog) listen on 127.0.0.1 only.
At most two battlefields run at once (one per open war-front axis); each instance uses about 1 GB
of RAM and a CPU core.

Settings live in one file: copy `huxley.conf.example` to `huxley.conf` and set what you need
(at least `HUXLEY_CLIENT_ROOT`, `HUXLEY_GAME_PUBLIC_IP` and `HUXLEY_GM_LOGINS` for a public
server). The example lists every setting with its default; an environment variable of the same
name overrides the file.

Behavior that intentionally differs from retail (for example every character gets the fully
expanded inventory and bank) is switched in `Network/ServerPolicy.cs`.

## Setting up the client and hosting

`docs/SETUP.md` walks through the whole setup: the server on Windows or Linux (with the systemd
service and battlefields under Wine), GM accounts, the two client INI lines players change, the
optional launcher patch server, backups and moving a server, and fixes for common problems. The
stock client plays unmodified; optional client improvements are in `docs/CLIENT_CHANGES.md`.

## Tests

```powershell
dotnet run --project Lunarite.Server.Tests\Lunarite.Server.Tests.csproj -c Debug
```

The suite prints one `PASS:` or `FAIL:` line per check: packet layouts against the client's,
data tables, persistence, and game flows over a real encrypted connection.

## Repository

| Path | Contents |
|---|---|
| `Lunarite.Server/` | The server (.NET 9). `docs/CODE-LAYOUT.md` explains its structure. |
| `Lunarite.Server.Tests/` | The test suite. |
| `data/` | Data tables extracted from the client's cooked packages. |
| `references/` | Provenance of the client builds and UE3 references used. |
| `docs/` | Project documentation; start at `docs/README.md`. |

## Conventions

- **Retail first.** Behavior comes from evidence: the client's native code (addresses are cited in
  comments), its UnrealScript and its data tables.
- **Native names.** Packets, handlers and the operations behind them carry the client's names
  (`docs/CODE-LAYOUT.md`, "Names").
- **No silent client changes.** Every client modification is recorded in `docs/CLIENT_CHANGES.md`.

## Credits

- **Eden** ([edenfps](https://github.com/edenfps)), who created and leads the project.
- **EDITz** ([EDITzDev](https://github.com/EDITzDev)), for their work on the private server.
- **pixellegolas**, for providing the Korean level packages.

## License

Licensed under the GNU Affero General Public License v3.0 (`LICENSE`). Third-party code keeps its own
license (`UELib`, `WaveFormRendererLib`, `references/external`). Huxley: The Dystopia and its assets belong to
their owners.

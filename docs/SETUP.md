# Setting up a server

This guide takes you from nothing to players logging in and fighting on battlefields. It covers a
server on Windows or Linux and the client players use. Every setting mentioned is in
`huxley.conf.example`.

## 1. What you need

- **The client**: the Webzen development build of June 2010 (`Binaries\HuxleyMMOGame.exe`,
  SHA-256 `916C8029AA0A784263749A9C0BF99D83AB9F54363E86EE619439870022611EAB`), plus the
  `LevelPackages` it is missing, taken from the Korean client
  (`research/ASSET_REFERENCE_VALIDATION.md`). Other builds are not supported: packet layouts and
  native addresses come from this one.
- **A machine for the server**, Windows or Linux (Ubuntu 24.04 is tested), with the .NET 9 SDK.
  It also needs its own copy of the client: battlefields run as the client's dedicated-server mode.
  The main server needs about 1 CPU core and 0.5 GB of RAM; each battlefield instance (at most two
  run at once) about 1 GB and a core. 4 cores and 8 GB is a comfortable start.
- **Ports open to players**:

  | Port | What |
  |---|---|
  | TCP 45405 | Login |
  | TCP 45406 | Game |
  | TCP 8090 | Launcher patch server (only if you run one) |
  | UDP 45407-45408 | NAT test the client runs at login |
  | UDP 56983 | Server list ping (signal bars) |
  | UDP 7788-7887 | Battlefield instances |

  Missions and Skirmish rooms are hosted by players, peer to peer; the server only introduces the
  players to each other (the NAT test above) and keeps the results.

## 2. Install the server

### Windows

```powershell
git clone <this repository>
cd <repository>
copy huxley.conf.example huxley.conf
notepad huxley.conf
dotnet run -c Release --project Lunarite.Server\Lunarite.Server.csproj
```

In `huxley.conf` set at least:

- `HUXLEY_CLIENT_ROOT`: the client folder on this machine, with no spaces in its path.
- `HUXLEY_GAME_PUBLIC_IP`: the address players reach you at (skip it for a LAN or a single PC).
- `HUXLEY_GM_LOGINS`: your GM login names.

The server finds `huxley.conf` by searching up from its build folder (or reads the file named by
`HUXLEY_CONFIG`). Its first log lines say which settings file it used.

### Linux

```bash
git clone <this repository>
cd <repository>
sudo bash deploy/linux/setup.sh
```

The script installs .NET 9, Wine (with 32-bit support) and Xvfb, copies the server to
`/opt/huxley/repo`, builds it and installs the `huxley` systemd service, which restarts the
server after a crash and starts it at boot. Then:

1. Copy the client to `/opt/huxley/client` (for example `rsync` or `scp` from a Windows machine).
2. Edit `/etc/huxley/huxley.env`. It is a `huxley.conf` in the same format; set
   `HUXLEY_GAME_PUBLIC_IP` and `HUXLEY_GM_LOGINS`.
3. `sudo chown -R huxley:huxley /opt/huxley`, then `sudo systemctl start huxley`.
4. Watch it with `journalctl -u huxley -f` or `tail -f /var/log/huxley/server.log`.

Battlefields run the client under Wine with a virtual display; no Windows machine is needed. The
first battlefield after a reboot takes about 1.5 minutes to start (later ones about 12 seconds), so
the server waits up to 120 seconds on Linux.

## 3. Accounts and GMs

Players register from the client's login screen. On an empty database the first login also
creates its account (`HUXLEY_PROVISION_FIRST_ACCOUNT=0` turns that off).

GM logins are reserved: nobody can register them. To create or reset one, set
`HUXLEY_RESET_ACCOUNT` and `HUXLEY_RESET_PASSWORD` in `huxley.conf`, start the server once, then
remove both lines. GMs get admin mode in the city and the chat commands `!notice`, `!kick`,
`!ban`/`!unban`, `!goto`, `!send`, `!back`, `!level`, `!give`, `!givepaid`, `!kit`, `!style`,
`!money`, `!pcroom` and `!simmap`.

## 4. The client players use

The stock client plays unmodified. Each player edits two files under `HuxleyMMOGame\Config\`
(they hold Korean text in a legacy encoding: change only these lines, and save without converting
the encoding):

- `HuxleyMMOGame.ini`, section `[HuxleyGame.HuxleySubsystem]`: `m_strMMOIP=` your server's
  address. The client sometimes rewrites this file from its defaults; set it again if it reverts
  to Webzen's old address.
- `HuxleyMMOEngine.ini`, section `[IpDrv.TcpNetDriver]`: `RelevantTimeout=20.0` and
  `SpawnPrioritySeconds=1.0`. Without them players joining a co-op mission or PvP match never get
  a character.

Then start `Binaries\HuxleyMMOGame.exe -LANGUAGE=INT`.

Optional client improvements (widescreen, a field-of-view slider, finer mouse sensitivity, a crash
guard for slow map loads, relit maps, the English text fill, ...) are described in
`CLIENT_CHANGES.md`. The patched client the main Lunarite server ships is available through that
server's launcher.

To give your players the same patched client: install the game once with the Lunarite launcher (it
downloads every patched file), then point both `HUXLEY_PATCH_ROOT` and `HUXLEY_CLIENT_ROOT` at copies
of that up-to-date folder. The patch server then serves those files to your own players, and your
battlefield instances run the same scripts they do.

### Delivering a client through the launcher (optional)

The rebuilt Webzen launcher can keep players' clients up to date from your server: the patch
server (TCP 8090) serves the files listed in `patch/manifest.txt` from `HUXLEY_PATCH_ROOT`, and
writes your address into the INIs it sends. Point `HUXLEY_PATCH_ROOT` at a folder that holds only
the files players should get, never at a client you play on (your settings and history would go
out with it). If you run a launcher, give the server's own client copy the same files, so
battlefield instances run the same scripts as players.

Set `HUXLEY_PATCH_BASE` to a clean US client as well and the launcher installs the whole game: the
patch server lists every file of that client (logs, screenshots and shortcuts left out) with the
payload's files in place of the base copies, and a launcher in an empty folder downloads all of it,
about 22 GB per new player. Plan bandwidth accordingly. The server checksums the client once and
keeps the results in `patch-version.txt.crc`, so restarts stay quick.

Korean players: a launcher on a Korean Windows says so (`Accept-Language: ko`), gets the base
client's Webzen Korean text and UI skin (`patch/korean.txt`) and starts the game in Korean. The
server sends that player its own few messages in Korean too.

## 5. Backups and moving a server

The server keeps everything in `huxley.db` (SQLite) next to `Lunarite.Server.dll` and writes a verified
snapshot every hour to `backups/` (168 kept by default).

To restore or move to another machine: stop the server, copy `huxley.db` from the newest folder in
`backups/` to the new database location, and delete any `huxley.db-wal` and `huxley.db-shm` there
first. A leftover write-ahead log is replayed over the copied database.

`dotnet Lunarite.Server.dll --stage-restore <snapshot folder> <new folder>` checks a snapshot and copies
it to a new folder without touching the running server.

## 6. Troubleshooting

| Symptom | Cause |
|---|---|
| Login works on the server PC but not for others | `HUXLEY_GAME_PUBLIC_IP` unset, or TCP 45405-45406 not open. |
| The client logs in to the wrong server | `m_strMMOIP` in `HuxleyMMOGame.ini` reverted; set it again. |
| Co-op guests or PvP players spawn with no character | `RelevantTimeout`/`SpawnPrioritySeconds` missing from the client's `HuxleyMMOEngine.ini`. |
| "You have failed to enter the loading room" on a battlefield | The instance did not start. On Linux check `HUXLEY_WINE` keeps `xvfb-run` (without a display it quits with "Please install DirectX 9.0c"); read `HuxleyMMOGame/Logs/Launch_Server.log` in the server's client folder. |
| "Server's version is outdated" joining a battlefield | The client folder path contains a space, so the instance fell back to the wrong network driver. Move the client to a path without spaces. |
| Battlefield joins time out for players outside your network | UDP 7788-7887 not open, or `HUXLEY_GAME_PUBLIC_IP` unset. |
| A battlefield instance is left running after a crash | Stop `HuxleyMMOGame.exe` processes whose command line has `SERVERS=1`; the systemd service and `Launch-HuxleyServer.ps1` do this on restart. |

#!/usr/bin/env bash
# One-time setup of a Huxley server on Ubuntu 24.04 or 26.04 (VPS or local VM). Run as root from a
# repo checkout:  sudo bash deploy/linux/setup.sh
# Then copy the client, payload and (optionally) huxley.db as printed at the end.
set -euo pipefail

REPO_SRC="$(cd "$(dirname "$0")/../.." && pwd)"
BASE=/opt/huxley

echo "== packages: .NET 9 SDK, Wine (32-bit), Xvfb"
dpkg --add-architecture i386
apt-get update
apt-get install -y ca-certificates curl rsync sqlite3 xvfb xauth
# 26.04 merged wine64 into wine; 24.04 still has it.
apt-get install -y wine wine32:i386 wine64 || apt-get install -y wine wine32:i386
# Ubuntu packages .NET 9 only up to 24.04 (26.04 ships .NET 10), so take Microsoft's installer
# when apt has no dotnet-sdk-9.0.
if ! apt-get install -y dotnet-sdk-9.0; then
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    bash /tmp/dotnet-install.sh --channel 9.0 --install-dir /usr/lib/dotnet9
    ln -sf /usr/lib/dotnet9/dotnet /usr/bin/dotnet
fi

echo "== user and folders"
id huxley >/dev/null 2>&1 || useradd --system --create-home --home-dir "$BASE" --shell /usr/sbin/nologin huxley
mkdir -p "$BASE"/{repo,client,clean-client,payload,backups,wine} /etc/huxley /var/log/huxley
# The server needs its source, data/ and patch/; research folders stay behind (~2.5 GB).
rsync -a --delete --exclude 'bin/' --exclude 'obj/' --exclude '.git/' --exclude 'backups/' \
    --exclude 'logs/' --exclude '*.log' \
    "$REPO_SRC/" "$BASE/repo/"
chown -R huxley:huxley "$BASE" /var/log/huxley

echo "== build"
sudo -u huxley -H dotnet build -c Release "$BASE/repo/Lunarite.Server/Lunarite.Server.csproj"

echo "== wine prefix"
sudo -u huxley -H env WINEPREFIX="$BASE/wine" WINEDEBUG=-all xvfb-run -a wineboot --init || true

echo "== service"
[ -f /etc/huxley/huxley.env ] || cp "$BASE/repo/deploy/linux/huxley.env.example" /etc/huxley/huxley.env
cp "$BASE/repo/deploy/linux/huxley.service" /etc/systemd/system/huxley.service
cp "$BASE/repo/deploy/linux/huxley-logrotate" /etc/logrotate.d/huxley
systemctl daemon-reload
systemctl enable huxley

cat <<EOF

Next:
  1. Copy the game client to $BASE/client   (e.g. rsync from C:\\Games\\Huxley)
  2. Copy the patch payload to $BASE/payload (C:\\Games\\Huxley_PatchPayload) and lay it over the
     client too (rsync -a $BASE/payload/ $BASE/client/): battlefield instances run the client's
     scripts, and the payload carries the player patches (no admin for non-GMs)
     Optional: copy an untouched clean US client to $BASE/clean-client (HUXLEY_PATCH_BASE) so the
     launcher installs the whole game for new players (about 22 GB each)
  3. Optional, existing players: copy huxley.db from the newest folder in the Windows server's backups/
     to "$BASE/repo/Lunarite.Server/bin/Release/net9.0/" with the service stopped
     (systemctl stop huxley), and delete any huxley.db-wal / huxley.db-shm there first:
     a leftover write-ahead log is replayed over the copied database.
  4. chown -R huxley:huxley $BASE ; edit /etc/huxley/huxley.env (HUXLEY_GAME_PUBLIC_IP)
  5. Open ports: TCP 45405, 45406, 8090; UDP 45407-45408, 56983, 7788-7887
  6. systemctl start huxley ; journalctl -u huxley -f ; tail -f /var/log/huxley/server.log
EOF

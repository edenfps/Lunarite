#!/bin/bash
# Pushes the newest local database backup (HUXLEY_BACKUP_DIR, written by the server every hour) to a private
# GitHub repository, encrypted to the "Lunarite DB backups" public key. The private key never lives on this
# host, so the repository holds nothing readable without it. Keeps the newest $KEEP files and one commit, so the
# repository stays small. Run by huxley-offsite-backup.timer. Needs in /etc/huxley/offsite: public.asc (the backup
# key's public half), deploy_key (a read-write deploy key for the repository); HUXLEY_OFFSITE_REPO in
# /etc/huxley/huxley.env names the repository.
set -euo pipefail
CONF=/etc/huxley/offsite
HUXLEY_OFFSITE_REPO=${HUXLEY_OFFSITE_REPO:-$(grep -E '^HUXLEY_OFFSITE_REPO=' /etc/huxley/huxley.env | cut -d= -f2- || true)}
REPO=${HUXLEY_OFFSITE_REPO:?set HUXLEY_OFFSITE_REPO to the backup repository, e.g. git@github.com:you/db-backups.git}
KEEP=${HUXLEY_OFFSITE_KEEP:-30}
BACKUPS=$(grep -E '^HUXLEY_BACKUP_DIR=' /etc/huxley/huxley.env | cut -d= -f2-)
BACKUPS=${BACKUPS:-/opt/huxley/backups}

latest=$(ls -1d "$BACKUPS"/*/ 2>/dev/null | sort | tail -1)
[ -n "$latest" ] && [ -f "$latest/huxley.db" ] || { echo "no local backup in $BACKUPS" >&2; exit 1; }
sqlite3 "$latest/huxley.db" 'PRAGMA integrity_check;' | grep -qx ok || { echo "integrity check failed: $latest" >&2; exit 1; }

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
export GNUPGHOME="$work/gnupg" GIT_SSH_COMMAND="ssh -i $CONF/deploy_key -o IdentitiesOnly=yes -o StrictHostKeyChecking=accept-new -o UserKnownHostsFile=$CONF/known_hosts"
mkdir -m 700 "$GNUPGHOME"
gpg --batch --quiet --import "$CONF/public.asc"
fpr=$(gpg --batch --with-colons --list-keys | awk -F: '/^fpr/ {print $10; exit}')

git clone --quiet --depth 1 "$REPO" "$work/repo" 2>/dev/null || { mkdir "$work/repo"; git -C "$work/repo" init --quiet -b main; git -C "$work/repo" remote add origin "$REPO"; }
cd "$work/repo"
name=huxley-$(basename "$latest" | cut -c1-15).db.gz.gpg
gzip -9c "$latest/huxley.db" | gpg --batch --yes --trust-model always --recipient "$fpr" --encrypt --output "$name"
ls -1 huxley-*.db.gz.gpg | sort | head -n -"$KEEP" | xargs -r rm -f
cat > README.md <<EOF
Encrypted Lunarite database backups, newest $KEEP, pushed daily by the VPS (deploy/linux/offsite-backup.sh).
Restore: gpg --decrypt <file> | gunzip > huxley.db (needs the "Lunarite DB backups" private key).
EOF
git checkout --quiet --orphan next
git add -A
git -c user.name="Lunarite VPS" -c user.email=backup@lunarite.invalid commit --quiet -m "Backups up to $name"
git push --quiet --force origin next:main
echo "pushed $name ($(ls -1 huxley-*.db.gz.gpg | wc -l) kept)"

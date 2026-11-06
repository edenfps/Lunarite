#!/bin/bash
# Discord server status for the webhook in /etc/huxley/alert.env (HUXLEY_ALERT_WEBHOOK=...). No webhook = no-op.
# One status message is kept and edited in place (its id in /var/lib/huxley-status/message-id):
#   alert.sh starting  - huxley.service ExecStartPost: waits until the login port listens, then "online"
#   alert.sh stop      - huxley.service ExecStopPost: "maintenance" for systemctl stop/restart, "down" for a crash
#   alert.sh failed <unit> - OnFailure of the off-site backup: a separate one-line message
# Messages carry no host name or log lines (those can hold player addresses and logins). The status
# message lists the character names online, which every player can see in game.
set -u
[ -r /etc/huxley/alert.env ] && . /etc/huxley/alert.env
[ -n "${HUXLEY_ALERT_WEBHOOK:-}" ] || exit 0
STATE=/var/lib/huxley-status
mkdir -p "$STATE"

post() {   # new message; prints its id
  curl -fsS -m 10 -H 'Content-Type: application/json' -d "$1" "$HUXLEY_ALERT_WEBHOOK?wait=true" | jq -r '.id // empty'
}

status() {   # edit the status message, or post a new one if it is gone
  local body id
  body=$(jq -nc --arg text "$1" '{content: $text, allowed_mentions: {parse: []}}')
  id=$(cat "$STATE/message-id" 2>/dev/null || true)
  if [ -n "$id" ] && curl -fsS -m 10 -X PATCH -H 'Content-Type: application/json' -d "$body" \
      "$HUXLEY_ALERT_WEBHOOK/messages/$id" >/dev/null 2>&1; then
    return
  fi
  id=$(post "$body") && [ -n "$id" ] && echo "$id" > "$STATE/message-id"
}

now=$(date +%s)

# Players online = established connections on the game port (45406) from outside this host; local
# battlefield instances connect over loopback and are not players.
players() {
  ss -Htn state established '( sport = :45406 )' 2>/dev/null | awk '{print $4}' \
    | grep -v -E '^(127\.|\[::1\]|\[::ffff:127\.)' | wc -l
}

names_line() {   # $1 = status file, $2 = JSON key, $3 = label: names without Discord markup, 60 at most
  jq -r --arg key "$2" --arg label "$3" '(.[$key] // []) as $n | if ($n | length) == 0 then empty else
    "**\($label):** " + ($n[:60] | map(gsub("(?<c>[*_~`|>])"; "\\"+.c)) | join(", "))
    + (if ($n | length) > 60 then " +\(($n | length) - 60) more" else "" end) end' "$1"
}

online_text() {   # $1 = since (unix time)
  local n plural=s sapiens= alternative= file updated names=
  # The server's per-faction count (HUXLEY_STATUS_FILE, rewritten every 15 s), unless it is missing or stale.
  file=$(grep -E '^HUXLEY_STATUS_FILE=' /etc/huxley/huxley.env 2>/dev/null | cut -d= -f2-)
  if [ -n "$file" ] && [ -r "$file" ]; then
    updated=$(jq -r '.updated // 0' "$file" 2>/dev/null || echo 0)
    if [ $(( now - updated )) -le 60 ]; then
      n=$(jq -r '.online' "$file"); sapiens=$(jq -r '.sapiens' "$file"); alternative=$(jq -r '.alternative' "$file")
      names=$(names_line "$file" sapiensNames Sapiens; names_line "$file" alternativeNames Alternative)
      [ -z "$names" ] || names+=$'\n'
    fi
  fi
  [ -n "${n:-}" ] || n=$(players)
  [ "$n" = 1 ] && plural=
  if [ -n "$sapiens" ]; then
    printf '🟢 **Lunarite is online** · %s player%s online (%s Sapiens · %s Alternative)\n%s-# since <t:%s:f>' \
      "$n" "$plural" "$sapiens" "$alternative" "$names" "$1"
  else
    printf '🟢 **Lunarite is online** · %s player%s online\n-# since <t:%s:f>' "$n" "$plural" "$1"
  fi
}

case "${1:-}" in
  starting)
    # The agent (login) port opens once the server has loaded; give it up to 3 minutes.
    for _ in $(seq 1 90); do
      ss -ltn 2>/dev/null | grep -q ':45405 ' && break
      sleep 2
    done
    echo "$now" > "$STATE/online-since"
    text=$(online_text "$now")
    status "$text"
    echo "$text" > "$STATE/last-text"
    ;;
  refresh)
    # huxley-status.timer: update the player count while the server is up; edit only on change.
    systemctl is-active --quiet huxley || exit 0
    ss -ltn 2>/dev/null | grep -q ':45405 ' || exit 0
    since=$(cat "$STATE/online-since" 2>/dev/null || echo "$now")
    text=$(online_text "$since")
    [ "$text" = "$(cat "$STATE/last-text" 2>/dev/null)" ] && exit 0
    status "$text"
    echo "$text" > "$STATE/last-text"
    ;;
  stop)
    if [ "${SERVICE_RESULT:-success}" = success ]; then
      status "🛠️ **Lunarite is under maintenance**: back in a few minutes"$'\n'"-# since <t:$now:f>"
    else
      status "🔴 **Lunarite is down**: restarting automatically"$'\n'"-# since <t:$now:f>"
    fi
    rm -f "$STATE/last-text"
    ;;
  failed)
    case "${2:-}" in
      huxley-offsite-backup*) text="⚠️ The daily database backup failed." ;;
      *) text="⚠️ ${2:-A service} failed." ;;
    esac
    post "$(jq -nc --arg text "$text" '{content: $text}')" >/dev/null
    ;;
  *)
    post "$(jq -nc --arg text "Lunarite: $*" '{content: $text}')" >/dev/null
    ;;
esac
exit 0

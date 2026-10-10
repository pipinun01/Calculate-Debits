#!/bin/sh
# Daily database dump.
#   backup.sh          loop: one dump every day at BACKUP_HOUR (container time zone)
#   backup.sh once     one dump right now, then exit
# Dumps are custom-format (pg_restore) files in /backups, older than BACKUP_KEEP_DAYS are removed.
# With BACKUP_CHAT_ID and BOT_TOKEN set, every dump is also sent to that Telegram chat as a document.
set -u

BACKUP_DIR=/backups
HOUR=${BACKUP_HOUR:-3}
KEEP=${BACKUP_KEEP_DAYS:-14}
# Telegram bots can upload files up to 50 MB.
TELEGRAM_LIMIT=$((49 * 1024 * 1024))

log() { echo "$(date '+%Y-%m-%d %H:%M:%S') $*"; }

run_backup() {
    file="$BACKUP_DIR/debits-$(date +%Y-%m-%d_%H%M).dump"
    if ! pg_dump --format=custom --no-owner --file="$file.tmp"; then
        log "pg_dump FAILED"
        rm -f "$file.tmp"
        return 1
    fi
    mv "$file.tmp" "$file"
    size=$(wc -c < "$file")
    log "dump written: $file ($size bytes)"

    find "$BACKUP_DIR" -name 'debits-*.dump' -mtime "+$KEEP" -delete

    if [ -n "${BACKUP_CHAT_ID:-}" ] && [ -n "${BOT_TOKEN:-}" ]; then
        if [ "$size" -gt "$TELEGRAM_LIMIT" ]; then
            log "dump is larger than 49 MB: not sent to Telegram, copy it off the server another way"
            curl -fsS -o /dev/null "https://api.telegram.org/bot${BOT_TOKEN}/sendMessage" \
                -d chat_id="$BACKUP_CHAT_ID" -d text="Debits: backup $(basename "$file") is ${size} bytes, too large for Telegram. It stays on the server only." || true
        elif curl -fsS -o /dev/null "https://api.telegram.org/bot${BOT_TOKEN}/sendDocument" \
                -F chat_id="$BACKUP_CHAT_ID" -F document=@"$file" -F caption="Debits backup $(date +%Y-%m-%d)" -F disable_notification=true; then
            log "dump sent to Telegram chat $BACKUP_CHAT_ID"
        else
            log "sending to Telegram FAILED (the dump is kept in $BACKUP_DIR)"
        fi
    fi
}

mkdir -p "$BACKUP_DIR"

if [ "${1:-}" = "once" ]; then
    run_backup
    exit $?
fi

log "backup loop started: every day at ${HOUR}:00, keeping ${KEEP} days"
last=""
while true; do
    today=$(date +%Y-%m-%d)
    # %-H is not portable to busybox: strip the leading zero by hand.
    hour=$(date +%H | sed 's/^0//')
    if [ "${hour:-0}" -ge "$HOUR" ] && [ "$last" != "$today" ]; then
        # A dump made earlier today (before a restart) counts.
        if ls "$BACKUP_DIR"/debits-"$today"_*.dump >/dev/null 2>&1; then
            last=$today
        elif run_backup; then
            last=$today
        fi
    fi
    sleep 600
done

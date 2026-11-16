#!/usr/bin/env bash
set -euo pipefail
# Manual / cron backup. Prefer sqlite3 .backup for a consistent snapshot.

DB_PATH="${1:-/var/lib/tradewithbeast/trade.sqlite}"
BACKUP_DIR="${2:-/var/lib/tradewithbeast/backups}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
DEST="${BACKUP_DIR}/trade-${STAMP}.sqlite"

mkdir -p "${BACKUP_DIR}"
if [[ ! -f "${DB_PATH}" ]]; then
  echo "ERROR: database file not found: ${DB_PATH}" >&2
  exit 1
fi
if command -v sqlite3 >/dev/null 2>&1; then
  sqlite3 "${DB_PATH}" ".backup '${DEST}'"
else
  cp -a "${DB_PATH}" "${DEST}"
fi
if [[ ! -f "${DEST}" ]] || [[ ! -s "${DEST}" ]]; then
  echo "ERROR: backup failed or empty: ${DEST}" >&2
  exit 1
fi
echo "Backup written: ${DEST}"
#!/usr/bin/env bash
set -euo pipefail
# Rollback API + static frontend to a previous release directory. Does NOT auto-restore DB unless RESTORE_DB is set.

PREVIOUS_API="${1:?Usage: rollback.sh /path/to/previous/api-publish}"
PREVIOUS_WEB="${2:-}"
LIVE_API="/opt/tradewithbeast/api"
LIVE_WEB="/var/www/tradewithbeast"
RESTORE_DB="${RESTORE_DB:-}"

echo "Stopping API..."
sudo systemctl stop tradewithbeast-api.service || true

if [[ -n "${RESTORE_DB}" ]]; then
  echo "Stopping writes: halting API only; restore DB manually:"
  echo "  sudo cp -a '${RESTORE_DB}' /var/lib/tradewithbeast/trade.sqlite"
  read -r -p "Press enter after DB restore (or Ctrl+C to abort)..."
fi

sudo rsync -a --delete "${PREVIOUS_API}/" "${LIVE_API}/"
if [[ -n "${PREVIOUS_WEB}" ]]; then
  sudo rsync -a --delete "${PREVIOUS_WEB}/" "${LIVE_WEB}/"
fi

sudo systemctl start tradewithbeast-api.service
echo "Verify: curl -fsS https://yourdomain.com/api/health"
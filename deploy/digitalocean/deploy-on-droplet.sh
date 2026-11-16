#!/usr/bin/env bash
set -euo pipefail
# Run on the droplet after uploading the release bundle (see upload-release.ps1).
# Does not overwrite SQLite data on /var/lib/tradewithbeast.

if [[ "${BASH_SOURCE[0]}" != "${0}" ]]; then
  echo "ERROR: Do not source this script — run: bash deploy-on-droplet.sh" >&2
  exit 1
fi

SCRIPT_PATH="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/$(basename "${BASH_SOURCE[0]}")"
if [[ ! -f "${SCRIPT_PATH}" ]]; then
  echo "ERROR: deploy-on-droplet.sh not found at expected path" >&2
  exit 1
fi

API_SRC="${1:-./artifacts/api}"
WEB_SRC="${2:-./artifacts/web}"
DATA_ROOT="${TWB_DATA_ROOT:-/var/lib/tradewithbeast}"
LIVE_API="/opt/tradewithbeast/api"
LIVE_WEB="/var/www/tradewithbeast"
DB_PATH="${TWB_DB_PATH:-${DATA_ROOT}/trade.sqlite}"

if ! mountpoint -q "${DATA_ROOT}" 2>/dev/null; then
  echo "ERROR: persistent volume not mounted at ${DATA_ROOT}" >&2
  exit 1
fi

for src in "${API_SRC}" "${WEB_SRC}"; do
  if [[ ! -d "${src}" ]]; then
    echo "ERROR: missing artifact directory: ${src}" >&2
    exit 1
  fi
done

if [[ -f "${API_SRC}/TradeWithBeast.dll" ]]; then
  if command -v jq >/dev/null 2>&1; then
    TFM="$(jq -r '.runtimeOptions.tfm // empty' "${API_SRC}/TradeWithBeast.runtimeconfig.json" 2>/dev/null || true)"
  else
    TFM="$(grep -o '"tfm": *"[^"]*"' "${API_SRC}/TradeWithBeast.runtimeconfig.json" | head -1 | sed 's/.*"\([^"]*\)"$/\1/')"
  fi
  case "${TFM}" in
    net10.0) ;;
    *)
      echo "ERROR: TradeWithBeast.dll targets unsupported framework: ${TFM:-unknown} (expected net10.0)" >&2
      exit 1
      ;;
  esac
fi

sudo mkdir -p "${DATA_ROOT}/backups" "${LIVE_API}" "${LIVE_WEB}"
sudo chown -R twb:twb "${DATA_ROOT}" "${LIVE_API}"

if [[ -f "${DB_PATH}" ]]; then
  echo "Preserving existing database: ${DB_PATH}"
  sudo chown twb:twb "${DB_PATH}"
else
  echo "No existing database at ${DB_PATH} (first install — API may create on start)"
fi

sudo systemctl stop tradewithbeast-api.service || true
sudo rsync -a --delete \
  --exclude 'trade.sqlite' \
  --exclude '*.sqlite' \
  "${API_SRC}/" "${LIVE_API}/"
sudo rsync -a --delete "${WEB_SRC}/" "${LIVE_WEB}/"
sudo chown -R twb:twb "${LIVE_API}"
sudo systemctl start tradewithbeast-api.service
sudo systemctl is-active --quiet tradewithbeast-api.service
sudo systemctl reload nginx || sudo systemctl start nginx

curl -fsS "http://127.0.0.1:8080/api/health" | head -c 200
echo
echo "Deploy complete. Verify HTTPS /api/health and admin deployment-readiness."
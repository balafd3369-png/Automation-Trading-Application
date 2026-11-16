#!/usr/bin/env bash
# Read-only infrastructure verification for Trade With Beast production host.
# Run on the droplet after provisioning. Does not modify configuration or data.
set -euo pipefail

PASS=0
FAIL=0
WARN=0
MANDATORY_FAILED=0

DATA_ROOT="${TWB_DATA_ROOT:-/var/lib/tradewithbeast}"
DOMAIN="${TWB_DOMAIN:-}"
CHECK_TLS="${TWB_CHECK_TLS:-0}"
MIN_RAM_MB="${TWB_MIN_RAM_MB:-7500}"
MIN_VCPU="${TWB_MIN_VCPU:-4}"

pass() { echo "PASS  $*"; PASS=$((PASS + 1)); }
fail() { echo "FAIL  $*"; FAIL=$((FAIL + 1)); MANDATORY_FAILED=1; }
warn() { echo "WARN  $*"; WARN=$((WARN + 1)); }

section() { echo ""; echo "=== $* ==="; }

section "Host identity"
if [[ -f /etc/os-release ]]; then
  # shellcheck source=/dev/null
  . /etc/os-release
  if [[ "${ID:-}" == "ubuntu" ]]; then
    pass "Linux distribution: ${PRETTY_NAME:-ubuntu}"
  else
    warn "Expected Ubuntu; found ID=${ID:-unknown} (${PRETTY_NAME:-})"
  fi
  if [[ "${VERSION_ID:-}" == "24.04" ]]; then
    pass "Ubuntu version 24.04 LTS"
  else
    warn "Recommended Ubuntu 24.04 LTS; VERSION_ID=${VERSION_ID:-unknown}"
  fi
else
  fail "Cannot read /etc/os-release"
fi

section "CPU and RAM"
VCPU_COUNT="$(nproc 2>/dev/null || echo 0)"
if [[ "${VCPU_COUNT}" -ge "${MIN_VCPU}" ]]; then
  pass "vCPU count: ${VCPU_COUNT} (min ${MIN_VCPU})"
else
  warn "vCPU count: ${VCPU_COUNT} (recommended >= ${MIN_VCPU})"
fi
MEM_KB="$(awk '/MemTotal:/ {print $2}' /proc/meminfo 2>/dev/null || echo 0)"
MEM_MB=$((MEM_KB / 1024))
if [[ "${MEM_MB}" -ge "${MIN_RAM_MB}" ]]; then
  pass "RAM: ${MEM_MB} MB (min ${MIN_RAM_MB} MB target for 8 GiB droplet)"
else
  warn "RAM: ${MEM_MB} MB (recommended >= ${MIN_RAM_MB} MB)"
fi

section "Disk space"
ROOT_AVAIL="$(df -Pm / 2>/dev/null | awk 'NR==2 {print $4}' || echo 0)"
if [[ "${ROOT_AVAIL}" -ge 10240 ]]; then
  pass "Root filesystem free: ${ROOT_AVAIL} MB"
else
  warn "Root filesystem free: ${ROOT_AVAIL} MB (low; prefer >= 10 GB free)"
fi

section "Persistent volume (${DATA_ROOT})"
if mountpoint -q "${DATA_ROOT}" 2>/dev/null; then
  pass "Mount point active: ${DATA_ROOT}"
else
  fail "Mount point not mounted: ${DATA_ROOT}"
fi
FSTAB_MATCHES="$(grep -E "[[:space:]]${DATA_ROOT}[[:space:]]" /etc/fstab 2>/dev/null | wc -l | tr -d ' ')"
if [[ "${FSTAB_MATCHES}" -eq 1 ]]; then
  pass "/etc/fstab contains single entry for ${DATA_ROOT}"
elif [[ "${FSTAB_MATCHES}" -gt 1 ]]; then
  fail "Duplicate /etc/fstab entries for ${DATA_ROOT}"
else
  warn "/etc/fstab has no entry for ${DATA_ROOT} (reboot may not remount)"
fi
DB_PATH="${TWB_DB_PATH:-${DATA_ROOT}/trade.sqlite}"
BACKUP_DIR="${DATA_ROOT}/backups"
if [[ -f "${DB_PATH}" ]]; then
  if [[ "$(readlink -f "$(dirname "${DB_PATH}")" 2>/dev/null || realpath "$(dirname "${DB_PATH}")" 2>/dev/null || echo "")" == *"${DATA_ROOT}"* ]] || [[ "${DB_PATH}" == "${DATA_ROOT}"* ]]; then
    pass "Database path on volume: ${DB_PATH}"
  else
    fail "Database path not under ${DATA_ROOT}: ${DB_PATH}"
  fi
else
  warn "Database not created yet: ${DB_PATH}"
fi
if [[ -d "${BACKUP_DIR}" ]]; then
  pass "Backup directory on volume: ${BACKUP_DIR}"
else
  fail "Missing backup directory: ${BACKUP_DIR}"
fi
for sub in "${DATA_ROOT}" "${DATA_ROOT}/backups"; do
  if [[ -d "${sub}" ]]; then
    pass "Directory exists: ${sub}"
  else
    fail "Missing directory: ${sub}"
  fi
done

section "Service account twb"
if id twb &>/dev/null; then
  pass "User twb exists"
else
  fail "User twb missing (run: sudo adduser --system --group twb)"
fi

section "Packages and runtime"
if command -v nginx >/dev/null 2>&1; then
  pass "nginx installed: $(nginx -v 2>&1 | head -1)"
else
  fail "nginx not installed"
fi
if command -v dotnet >/dev/null 2>&1; then
  DOTNET_VER="$(dotnet --list-runtimes 2>/dev/null | grep -i 'Microsoft.AspNetCore.App 10' || true)"
  if [[ -n "${DOTNET_VER}" ]]; then
    pass "ASP.NET Core 10 runtime present"
  else
    fail "dotnet found but Microsoft.AspNetCore.App 10.x runtime missing"
  fi
else
  fail "dotnet CLI not found (install aspnetcore-runtime-10.0)"
fi
if command -v certbot >/dev/null 2>&1; then
  pass "certbot installed"
else
  warn "certbot not installed (required before HTTPS)"
fi
if command -v sqlite3 >/dev/null 2>&1; then
  pass "sqlite3 CLI available (recommended for backups)"
else
  warn "sqlite3 CLI not installed (backup falls back to file copy)"
fi

section "SSH"
if systemctl is-active --quiet ssh 2>/dev/null || systemctl is-active --quiet sshd 2>/dev/null; then
  pass "SSH service active"
else
  fail "SSH service not active"
fi

section "Firewall"
if command -v ufw >/dev/null 2>&1; then
  UFW_STATUS="$(sudo ufw status 2>/dev/null || ufw status 2>/dev/null || true)"
  if echo "${UFW_STATUS}" | grep -qi "Status: active"; then
    pass "ufw is active"
  else
    warn "ufw present but not active"
  fi
else
  warn "ufw not installed (use DigitalOcean Cloud Firewall or install ufw)"
fi

section "Listening ports"
if ss -lnt 2>/dev/null | grep -q ':22 '; then
  pass "Port 22/tcp listening (SSH)"
else
  fail "Port 22/tcp not listening"
fi
if ss -lnt 2>/dev/null | grep -q ':80 '; then
  pass "Port 80/tcp listening (HTTP)"
else
  warn "Port 80/tcp not listening (nginx not started or not configured)"
fi
if [[ "${CHECK_TLS}" == "1" ]]; then
  if ss -lnt 2>/dev/null | grep -q ':443 '; then
    pass "Port 443/tcp listening (HTTPS)"
  else
    fail "Port 443/tcp not listening (CHECK_TLS=1)"
  fi
fi
if ss -lnt 2>/dev/null | grep ':8080' | grep -qvE '127\.0\.0\.1:8080|\[::1\]:8080'; then
  warn "Port 8080 exposed on non-loopback interface (should be localhost only behind nginx)"
elif ss -lnt 2>/dev/null | grep -q ':8080'; then
  pass "Port 8080 bound on loopback (expected for API behind nginx)"
else
  warn "Port 8080 not listening (API not started yet — OK before first deploy)"
fi

section "DNS (optional)"
if [[ -n "${DOMAIN}" ]]; then
  if command -v getent >/dev/null 2>&1; then
    if getent ahosts "${DOMAIN}" >/dev/null 2>&1; then
      pass "DNS resolves: ${DOMAIN}"
    else
      fail "DNS does not resolve: ${DOMAIN}"
    fi
  else
    warn "getent not available; skipping DNS check"
  fi
else
  warn "TWB_DOMAIN not set — skipping DNS check"
fi

section "TLS (optional)"
ENV_FILE="${TWB_ENV_FILE:-/etc/tradewithbeast/environment}"
if [[ "${CHECK_TLS}" == "1" && -n "${DOMAIN}" ]]; then
  CERT_DIR="/etc/letsencrypt/live/${DOMAIN}"
  if [[ -f "${CERT_DIR}/fullchain.pem" && -f "${CERT_DIR}/privkey.pem" ]]; then
    pass "Let's Encrypt certificates present for ${DOMAIN}"
  else
    fail "TLS certificates missing under ${CERT_DIR}"
  fi
elif [[ "${CHECK_TLS}" == "1" ]]; then
  warn "CHECK_TLS=1 but TWB_DOMAIN empty"
else
  warn "TWB_CHECK_TLS not set — skipping TLS certificate check"
fi

section "Production environment file (presence only)"
if [[ -f "${ENV_FILE}" ]]; then
  if [[ "$(stat -c '%a' "${ENV_FILE}" 2>/dev/null || stat -f '%OLp' "${ENV_FILE}" 2>/dev/null)" == "600" ]]; then
    pass "Environment file exists with mode 600: ${ENV_FILE}"
  else
    warn "Environment file exists but permissions are not 600: ${ENV_FILE}"
  fi
  if grep -q 'REPLACE_WITH' "${ENV_FILE}" 2>/dev/null; then
    fail "Environment file still contains REPLACE_WITH placeholders"
  else
    pass "No REPLACE_WITH placeholders in environment file"
  fi
  if grep -q '^LiveTrading__Enabled=false' "${ENV_FILE}" 2>/dev/null; then
    pass "LiveTrading__Enabled=false in environment file"
  else
    fail "LiveTrading__Enabled must be false in ${ENV_FILE}"
  fi
else
  warn "Environment file not found: ${ENV_FILE} (required before API start)"
fi

section "Summary"
echo "PASS=${PASS} FAIL=${FAIL} WARN=${WARN}"
if [[ "${MANDATORY_FAILED}" -ne 0 ]]; then
  echo "Overall: FAIL (mandatory prerequisites missing)"
  exit 1
fi
echo "Overall: PASS (no mandatory failures; review WARN items)"
exit 0
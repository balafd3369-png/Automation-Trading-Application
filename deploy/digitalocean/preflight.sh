#!/usr/bin/env bash
# Deployment preflight — local workstation or CI. Read-only; does not rebuild artifacts.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
ARTIFACTS="${SCRIPT_DIR}/artifacts"
MANIFEST="${ARTIFACTS}/build-manifest.json"
BUNDLE="${ARTIFACTS}/bundle"
RUNTIME_CFG="${ARTIFACTS}/api/TradeWithBeast.runtimeconfig.json"
SUPPORTED_TFM="net10.0"

PASS=0
FAIL=0
WARN=0
exit_code=0

pass() { echo "PASS  $*"; PASS=$((PASS + 1)); }
fail() { echo "FAIL  $*"; FAIL=$((FAIL + 1)); exit_code=1; }
warn() { echo "WARN  $*"; WARN=$((WARN + 1)); }

section() { echo ""; echo "=== $* ==="; }

sha256_file() {
  local f="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$f" | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$f" | awk '{print $1}'
  else
    echo ""
  fi
}

section "Repository"
if git -C "${REPO_ROOT}" rev-parse --is-inside-work-tree &>/dev/null; then
  pass "Git repository: ${REPO_ROOT}"
  COMMIT="$(git -C "${REPO_ROOT}" rev-parse HEAD)"
  BRANCH="$(git -C "${REPO_ROOT}" rev-parse --abbrev-ref HEAD)"
  echo "      commit: ${COMMIT}"
  echo "      branch: ${BRANCH}"
  if [[ -n "$(git -C "${REPO_ROOT}" status --porcelain)" ]]; then
    warn "Working tree has uncommitted changes (consider tagging release commit)"
    git -C "${REPO_ROOT}" status -sb | head -20
  else
    pass "Working tree clean"
  fi
else
  fail "Not a git repository: ${REPO_ROOT}"
fi

section "Backend target framework"
for csproj in "${REPO_ROOT}/backend/TradeWithBeast.csproj" "${REPO_ROOT}/backend/tests/TradeWithBeast.Tests/TradeWithBeast.Tests.csproj"; do
  if [[ -f "${csproj}" ]]; then
    if grep -q "<TargetFramework>${SUPPORTED_TFM}</TargetFramework>" "${csproj}"; then
      pass "TargetFramework ${SUPPORTED_TFM}: $(basename "${csproj}")"
    elif grep -q '<TargetFramework>net8.0</TargetFramework>' "${csproj}"; then
      fail "Unsupported TargetFramework net8.0 in $(basename "${csproj}") — upgrade to ${SUPPORTED_TFM}"
    else
      fail "TargetFramework not ${SUPPORTED_TFM} in $(basename "${csproj}")"
    fi
  else
    fail "Missing ${csproj}"
  fi
done

if [[ -f "${RUNTIME_CFG}" ]]; then
  if grep -q "\"tfm\": \"${SUPPORTED_TFM}\"" "${RUNTIME_CFG}" || grep -q "\"tfm\":\"${SUPPORTED_TFM}\"" "${RUNTIME_CFG}"; then
    pass "Published artifact tfm=${SUPPORTED_TFM}"
  else
    fail "Artifact TradeWithBeast.runtimeconfig.json is not ${SUPPORTED_TFM} — run deploy-release.ps1"
  fi
else
  fail "Missing ${RUNTIME_CFG}"
fi

section "Deployment scripts"
for f in deploy-release.ps1 upload-release.ps1 deploy-on-droplet.sh backup-sqlite.sh rollback.sh \
  verify-infrastructure.sh provision-persistent-volume.sh env.template \
  nginx-tradewithbeast.conf tradewithbeast-api.service; do
  if [[ -f "${SCRIPT_DIR}/${f}" ]]; then
    pass "Found ${f}"
  else
    fail "Missing ${SCRIPT_DIR}/${f}"
  fi
done

if grep -q 'RequiresMountsFor=/var/lib/tradewithbeast' "${SCRIPT_DIR}/tradewithbeast-api.service"; then
  pass "systemd unit requires persistent mount"
else
  fail "tradewithbeast-api.service missing RequiresMountsFor=/var/lib/tradewithbeast"
fi

if grep -q 'mkfs' "${SCRIPT_DIR}/provision-persistent-volume.sh" && grep -q 'TWB_CONFIRM_FORMAT=YES' "${SCRIPT_DIR}/provision-persistent-volume.sh"; then
  pass "Volume provision script requires explicit format confirmation"
else
  fail "provision-persistent-volume.sh does not enforce safe formatting rules"
fi

section "Artifacts"
if [[ -f "${ARTIFACTS}/api/TradeWithBeast.dll" ]]; then
  pass "Backend artifact: TradeWithBeast.dll"
else
  fail "Missing ${ARTIFACTS}/api/TradeWithBeast.dll — run deploy-release.ps1"
fi
if [[ -f "${ARTIFACTS}/web/index.html" ]]; then
  pass "Frontend artifact: web/index.html"
else
  fail "Missing ${ARTIFACTS}/web/index.html — run deploy-release.ps1"
fi
if [[ -f "${MANIFEST}" ]]; then
  pass "build-manifest.json present"
  if command -v jq >/dev/null 2>&1; then
    MANIFEST_COMMIT="$(jq -r '.gitCommit // empty' "${MANIFEST}")"
    LIVE="$(jq -r '.liveTradingEnabled // empty' "${MANIFEST}")"
    BACKEND="$(jq -r '.backend // empty' "${MANIFEST}")"
    echo "      manifest commit: ${MANIFEST_COMMIT}"
    echo "      backend: ${BACKEND}"
    if [[ "${LIVE}" == "false" ]]; then
      pass "Manifest liveTradingEnabled=false"
    else
      fail "Manifest liveTradingEnabled must be false (got: ${LIVE})"
    fi
    if [[ "${BACKEND}" == *"net10.0"* ]]; then
      pass "Manifest backend references net10.0"
    else
      fail "Manifest backend must reference net10.0 (got: ${BACKEND})"
    fi
    DLL_HASH="$(jq -r '.checksums.TradeWithBeastDll // empty' "${MANIFEST}")"
    WEB_HASH="$(jq -r '.checksums.webIndexHtml // empty' "${MANIFEST}")"
    if [[ -n "${DLL_HASH}" && -n "${WEB_HASH}" ]]; then
      ACTUAL_DLL="$(sha256_file "${ARTIFACTS}/api/TradeWithBeast.dll")"
      ACTUAL_WEB="$(sha256_file "${ARTIFACTS}/web/index.html")"
      if [[ -n "${ACTUAL_DLL}" && "${ACTUAL_DLL}" == "${DLL_HASH}" ]]; then
        pass "TradeWithBeast.dll checksum matches manifest"
      else
        fail "TradeWithBeast.dll checksum mismatch (rebuild with deploy-release.ps1)"
      fi
      if [[ -n "${ACTUAL_WEB}" && "${ACTUAL_WEB}" == "${WEB_HASH}" ]]; then
        pass "web/index.html checksum matches manifest"
      else
        fail "web/index.html checksum mismatch"
      fi
    else
      fail "Manifest missing checksums — run deploy-release.ps1"
    fi
  else
    if grep -q '"liveTradingEnabled":  *false' "${MANIFEST}" || grep -q '"liveTradingEnabled":false' "${MANIFEST}"; then
      pass "Manifest liveTradingEnabled=false (grep)"
    else
      fail "Could not confirm liveTradingEnabled=false in manifest"
    fi
    if grep -q 'net10.0' "${MANIFEST}"; then
      pass "Manifest backend references net10.0 (grep)"
    else
      fail "Manifest backend must reference net10.0"
    fi
    DLL_HASH="$(grep -o '"TradeWithBeastDll"[[:space:]]*:[[:space:]]*"[a-f0-9]*"' "${MANIFEST}" | head -1 | sed 's/.*"\([a-f0-9]*\)"$/\1/')"
    WEB_HASH="$(grep -o '"webIndexHtml"[[:space:]]*:[[:space:]]*"[a-f0-9]*"' "${MANIFEST}" | head -1 | sed 's/.*"\([a-f0-9]*\)"$/\1/')"
    if [[ -n "${DLL_HASH}" && -n "${WEB_HASH}" ]]; then
      ACTUAL_DLL="$(sha256_file "${ARTIFACTS}/api/TradeWithBeast.dll")"
      ACTUAL_WEB="$(sha256_file "${ARTIFACTS}/web/index.html")"
      if [[ -n "${ACTUAL_DLL}" && "${ACTUAL_DLL}" == "${DLL_HASH}" ]]; then
        pass "TradeWithBeast.dll checksum matches manifest"
      else
        fail "TradeWithBeast.dll checksum mismatch"
      fi
      if [[ -n "${ACTUAL_WEB}" && "${ACTUAL_WEB}" == "${WEB_HASH}" ]]; then
        pass "web/index.html checksum matches manifest"
      else
        fail "web/index.html checksum mismatch"
      fi
    else
      fail "Manifest missing checksums — run deploy-release.ps1"
    fi
  fi
else
  fail "Missing ${MANIFEST}"
fi

if [[ -f "${BUNDLE}/deploy-on-droplet.sh" ]]; then
  pass "Release bundle contains deploy-on-droplet.sh"
else
  fail "Missing ${BUNDLE}/deploy-on-droplet.sh — run deploy-release.ps1"
fi

section "Artifact integrity (basic)"
if [[ -f "${ARTIFACTS}/api/TradeWithBeast.dll" ]]; then
  SIZE="$(stat -c '%s' "${ARTIFACTS}/api/TradeWithBeast.dll" 2>/dev/null || stat -f '%z' "${ARTIFACTS}/api/TradeWithBeast.dll")"
  if [[ "${SIZE}" -gt 100000 ]]; then
    pass "TradeWithBeast.dll size plausible (${SIZE} bytes)"
  else
    warn "TradeWithBeast.dll unusually small (${SIZE} bytes)"
  fi
fi
WEB_FILES="$(find "${ARTIFACTS}/web" -type f 2>/dev/null | wc -l | tr -d ' ')"
if [[ "${WEB_FILES}" -gt 10 ]]; then
  pass "Frontend bundle file count: ${WEB_FILES}"
else
  warn "Few frontend files (${WEB_FILES}); build may be incomplete"
fi

section "Production configuration (repo)"
PROD_JSON="${REPO_ROOT}/backend/appsettings.Production.json"
if [[ -f "${PROD_JSON}" ]]; then
  if grep -q '"Enabled": false' "${PROD_JSON}" && grep -q 'LiveTrading' "${PROD_JSON}"; then
    pass "appsettings.Production.json has LiveTrading.Enabled false"
  else
    fail "LiveTrading.Enabled must be false in appsettings.Production.json"
  fi
  if grep -q '"RequireAuthentication": true' "${PROD_JSON}"; then
    pass "RequireAuthentication true in Production appsettings"
  else
    fail "RequireAuthentication must be true in appsettings.Production.json"
  fi
  if grep -q '"RequirePersistentDataMount": true' "${PROD_JSON}"; then
    pass "RequirePersistentDataMount true in Production appsettings"
  else
    fail "RequirePersistentDataMount must be true for single-host volume safety"
  fi
else
  fail "Missing ${PROD_JSON}"
fi

section "Environment template completeness"
ENV_TEMPLATE="${SCRIPT_DIR}/env.template"
if [[ -f "${ENV_TEMPLATE}" ]]; then
  for key in LiveTrading__Enabled=false ASPNETCORE_URLS=http://127.0.0.1:8080 \
    Database__SqlitePath=/var/lib/tradewithbeast/trade.sqlite; do
    if grep -q "^${key}" "${ENV_TEMPLATE}"; then
      pass "env.template contains ${key%%=*}"
    else
      fail "env.template missing ${key%%=*}"
    fi
  done
  if grep -q '^LiveTrading__Enabled=true' "${ENV_TEMPLATE}"; then
    fail "env.template must not enable live trading"
  fi
else
  fail "Missing env.template"
fi

section "Summary"
echo "PASS=${PASS} FAIL=${FAIL} WARN=${WARN}"
if [[ "${exit_code}" -ne 0 ]]; then
  echo "Preflight: FAIL — fix issues before upload/deploy"
  exit 1
fi
echo "Preflight: PASS — artifacts ready for upload (infrastructure must still be verified on server)"
exit 0
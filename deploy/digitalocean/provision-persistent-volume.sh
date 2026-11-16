#!/usr/bin/env bash
# Safe DigitalOcean block volume provisioning — never formats without explicit operator confirmation.
# Run on the server as root or with sudo. Does not deploy application binaries.
set -euo pipefail

DATA_ROOT="${TWB_DATA_ROOT:-/var/lib/tradewithbeast}"
DEVICE="${TWB_VOLUME_DEVICE:-}"
FSTAB="/etc/fstab"
CONFIRM_FORMAT="${TWB_CONFIRM_FORMAT:-}"

usage() {
  echo "Usage: TWB_VOLUME_DEVICE=/dev/disk/by-id/scsi-0DO_Volume_xxx [$0 flags]" >&2
  echo "  --mount-only    Mount existing filesystem (default if device has a signature)" >&2
  echo "  --format-new    mkfs only when TWB_CONFIRM_FORMAT=YES and device has no data signature" >&2
  echo "  --verify        Check mount, fstab, and required directories only" >&2
  exit 1
}

log() { echo "$*"; }
fail() { echo "ERROR: $*" >&2; exit 1; }

require_root() {
  if [[ "${EUID:-$(id -u)}" -ne 0 ]]; then
    fail "Run as root (sudo $0 ...)"
  fi
}

device_has_signature() {
  local dev="$1"
  if sudo blkid "${dev}" &>/dev/null; then
    return 0
  fi
  if sudo file -s "${dev}" 2>/dev/null | grep -qiE 'filesystem|ext[234]|xfs|btrfs'; then
    return 0
  fi
  return 1
}

fstab_has_data_root() {
  grep -E "[[:space:]]${DATA_ROOT//\//\\/}[[:space:]]" "${FSTAB}" 2>/dev/null || true
}

add_fstab_entry() {
  local uuid="$1"
  if fstab_has_data_root | grep -q "UUID=${uuid}"; then
    log "fstab already contains UUID=${uuid} for ${DATA_ROOT}"
    return 0
  fi
  if fstab_has_data_root | grep -q "${DATA_ROOT}"; then
    fail "fstab already has a different entry for ${DATA_ROOT}; resolve manually before re-running"
  fi
  echo "UUID=${uuid} ${DATA_ROOT} ext4 defaults,nofail,discard 0 2" | sudo tee -a "${FSTAB}" >/dev/null
  log "Added fstab entry for UUID=${uuid}"
}

mount_device() {
  local dev="$1"
  sudo mkdir -p "${DATA_ROOT}"
  if mountpoint -q "${DATA_ROOT}"; then
    log "Already mounted: ${DATA_ROOT}"
    return 0
  fi
  sudo mount "${dev}" "${DATA_ROOT}"
  log "Mounted ${dev} at ${DATA_ROOT}"
}

verify_post_reboot() {
  if ! mountpoint -q "${DATA_ROOT}"; then
    fail "Data root is not mounted: ${DATA_ROOT}"
  fi
  for sub in "${DATA_ROOT}" "${DATA_ROOT}/backups"; do
    [[ -d "${sub}" ]] || fail "Missing directory: ${sub}"
  done
  log "VERIFY PASS: ${DATA_ROOT} mounted with backups directory"
}

ACTION="${1:-}"

if [[ -z "${DEVICE}" && "${ACTION}" != "--verify" ]]; then
  usage
fi

require_root

case "${ACTION}" in
  --verify)
    verify_post_reboot
  if ! grep -qE "[[:space:]]${DATA_ROOT}[[:space:]]" "${FSTAB}" 2>/dev/null; then
      fail "fstab has no entry for ${DATA_ROOT}"
    fi
    log "VERIFY PASS: fstab contains ${DATA_ROOT}"
    exit 0
    ;;
  --format-new)
    if [[ "${CONFIRM_FORMAT}" != "YES" ]]; then
      fail "Refusing to format. Set TWB_CONFIRM_FORMAT=YES after reviewing the target device."
    fi
    if device_has_signature "${DEVICE}"; then
      fail "Device appears to contain a filesystem signature — refusing to format: ${DEVICE}"
    fi
    read -r -p "Type the device path again to confirm format (${DEVICE}): " CONFIRM_DEV
    if [[ "${CONFIRM_DEV}" != "${DEVICE}" ]]; then
      fail "Device confirmation mismatch"
    fi
    sudo mkfs.ext4 -L twbdata -E lazy_itable_init=0,lazy_journal_init=0 "${DEVICE}"
    log "Formatted ${DEVICE} (new empty volume)"
    ;;
  --mount-only|"")
    if ! device_has_signature "${DEVICE}"; then
      fail "No filesystem on ${DEVICE}. Use --format-new only on a new empty volume with TWB_CONFIRM_FORMAT=YES"
    fi
    ;;
  *)
    usage
    ;;
esac

if [[ -n "${DEVICE}" ]]; then
  mount_device "${DEVICE}"
  UUID="$(sudo blkid -s UUID -o value "${DEVICE}")"
  [[ -n "${UUID}" ]] || fail "Could not read UUID for ${DEVICE}"
  if [[ "$(sudo blkid -s UUID -o value "${DEVICE}")" != "${UUID}" ]]; then
    fail "UUID verification failed for ${DEVICE}"
  fi
  add_fstab_entry "${UUID}"
  sudo mkdir -p "${DATA_ROOT}/backups"
  if id twb &>/dev/null; then
    sudo chown -R twb:twb "${DATA_ROOT}"
  fi
  verify_post_reboot
fi
# Phase 9B — DigitalOcean provisioning (no deploy)

**Goal:** Production host ready with **`verify-infrastructure.sh` mandatory PASS** before any artifact upload.  
**Constraints:** `LiveTrading__Enabled=false`; no upload/deploy until step 8 passes.

Prerequisites: local tag `v1.0.0-restricted-prod-2026.11.16` pushed; checksums verified per `docs/RELEASE-CHECKSUMS.json`.

---

## Checkpoint gates

| Gate | Must pass before next step |
|------|----------------------------|
| G0 | Release tag on GitHub; `deploy-release.ps1` checksums match `RELEASE-CHECKSUMS.json` |
| G1 | Droplet + volume + SSH + DNS configured |
| G2 | Packages: nginx, ufw, certbot, **aspnetcore-runtime-10.0** |
| G3 | `verify-infrastructure.sh` — **0 mandatory FAIL** |
| G4 | Only then: Phase 9C upload + `deploy-on-droplet.sh` (separate runbook) |

---

## Operator sequence

### 1. Push release (workstation)

```powershell
cd Automation-Trading-Application
git push origin dev/v2-multi-broker
git push origin v1.0.0-restricted-prod-2026.11.16
.\deploy\digitalocean\deploy-release.ps1
# Compare SHA-256 to docs/RELEASE-CHECKSUMS.json
```

### 2. Create Droplet (DigitalOcean dashboard)

- Ubuntu **24.04 LTS**, **4 vCPU / 8 GB** recommended
- Same region as your users (e.g. BLR1)
- **SSH key** only (no password-only)
- Record public IPv4

See: `docs/PHASE9A-DIGITALOCEAN-INFRASTRUCTURE-GUIDE.md` §2–4

### 3. Attach persistent volume (50 GB+)

- Same region as droplet
- Attach to droplet

On server — **never auto-format**:

```bash
export TWB_VOLUME_DEVICE=/dev/disk/by-id/scsi-0DO_Volume_<id>
sudo bash /path/to/provision-persistent-volume.sh --mount-only
# New volume only: TWB_CONFIRM_FORMAT=YES then --format-new
sudo bash provision-persistent-volume.sh --verify   # after reboot test
```

### 4. SSH key authentication (workstation)

```powershell
.\deploy\digitalocean\verify-ssh.ps1 -Server <DROPLET_IP> -IdentityFile $env:USERPROFILE\.ssh\<key>
```

### 5. Domain and DNS

- A record → droplet IPv4
- Choose production hostname (replace `yourdomain.com` in nginx template later)

### 6. Install .NET 10 runtime (server)

```bash
wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
sudo dpkg -i /tmp/packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y aspnetcore-runtime-10.0 nginx certbot python3-certbot-nginx ufw sqlite3 rsync curl
dotnet --list-runtimes | grep 'AspNetCore.App 10'
```

### 7. Nginx, firewall, HTTPS

```bash
sudo ufw default deny incoming
sudo ufw allow OpenSSH
sudo ufw allow 'Nginx Full'
sudo ufw enable
# Copy tradewithbeast-api.service, nginx-tradewithbeast.conf from release bundle (after 9C upload) or repo
# Edit server_name; certbot after DNS propagates:
sudo certbot --nginx -d <your-hostname>
```

### 8. Infrastructure verification (server) — **required before upload**

```bash
export TWB_DATA_ROOT=/var/lib/tradewithbeast
bash verify-infrastructure.sh
TWB_DOMAIN=<hostname> TWB_CHECK_TLS=1 TWB_ENV_FILE=/etc/tradewithbeast/environment bash verify-infrastructure.sh
```

**Stop if any mandatory FAIL.** Create `/etc/tradewithbeast/environment` from `env.template` (chmod 600, `LiveTrading__Enabled=false`) before API start tests.

---

## Explicitly not in Phase 9B

- `upload-release.ps1` / `deploy-on-droplet.sh`
- Enabling live trading
- Writing production SQLite data

---

## After 9B

When G3 passes, proceed to **Phase 9C — restricted production deploy** (`deploy/digitalocean/PRODUCTION-RUNBOOK.md` upload steps only).
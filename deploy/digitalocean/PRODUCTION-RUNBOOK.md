# DigitalOcean single-host production runbook

**Restricted production:** `LiveTrading__Enabled=false` until separate safety approval.

Supporting docs: [`docs/PHASE9A-DIGITALOCEAN-INFRASTRUCTURE-GUIDE.md`](../../docs/PHASE9A-DIGITALOCEAN-INFRASTRUCTURE-GUIDE.md), [`docs/PHASE9A-PRODUCTION-SECRETS-GUIDE.md`](../../docs/PHASE9A-PRODUCTION-SECRETS-GUIDE.md).

---

## End-to-end sequence

| Step | Action | Where |
|------|--------|--------|
| 1 | Provision Droplet (Ubuntu 24.04, 4 vCPU / 8 GB recommended) | DigitalOcean dashboard |
| 2 | Attach persistent volume | DO dashboard |
| 3 | Mount volume via `provision-persistent-volume.sh`, verify after reboot, `twb` ownership | Server SSH |
| 4 | Configure SSH keys; test with `verify-ssh.ps1` | Workstation |
| 5 | Run `verify-infrastructure.sh` (baseline) | Server |
| 6 | Configure DNS A record → droplet IP | DNS provider |
| 7 | Install nginx, .NET **10** runtime (`aspnetcore-runtime-10.0`), certbot, ufw | Server |
| 8 | Configure TLS (`certbot --nginx`) | Server |
| 9 | Configure production secrets (`env.template` → `/etc/tradewithbeast/environment`, chmod 600) | Server |
| 10 | Run `preflight.sh` on workstation | Workstation |
| 11 | Upload bundle (`upload-release.ps1` → api, web, deploy scripts, nginx, systemd) | Workstation |
| 12 | Execute `deploy-on-droplet.sh` | Server |
| 13 | Verify application health (`/api/health`, deployment-readiness) | HTTPS |
| 14 | Verify DB on volume, restart API, confirm data persists | Server |
| 15 | Verify monitoring (10 min comprehensive), backup script, external uptime | Server + monitor |

---

## First-time server install (detail)

```bash
sudo adduser --system --group twb
sudo mkdir -p /etc/tradewithbeast /opt/tradewithbeast/api /var/www/tradewithbeast
# Volume mount — see infrastructure guide
sudo cp env.template /etc/tradewithbeast/environment
sudo chmod 600 /etc/tradewithbeast/environment
# Edit secrets — see PHASE9A-PRODUCTION-SECRETS-GUIDE.md

sudo cp tradewithbeast-api.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable tradewithbeast-api.service

sudo cp nginx-tradewithbeast.conf /etc/nginx/sites-available/tradewithbeast
# Replace yourdomain.com with real hostname
sudo ln -sf /etc/nginx/sites-available/tradewithbeast /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
sudo certbot --nginx -d <your-hostname>
```

---

## Workstation: build and preflight

```powershell
cd Automation-Trading-Application
# Git Bash or WSL:
bash deploy/digitalocean/preflight.sh

# Rebuild if needed:
.\deploy\digitalocean\deploy-release.ps1
```

---

## Workstation: SSH verify

```powershell
.\deploy\digitalocean\verify-ssh.ps1 -Server <DROPLET_IP> -User root -IdentityFile $env:USERPROFILE\.ssh\<your_key>
```

---

## Server: infrastructure verify

```bash
bash verify-infrastructure.sh
TWB_DOMAIN=<hostname> TWB_CHECK_TLS=1 TWB_ENV_FILE=/etc/tradewithbeast/environment bash verify-infrastructure.sh
```

---

## Deploy new release

1. `preflight.sh` → PASS  
2. Upload `/tmp/twb-release/api` and `web`  
3. `bash deploy-on-droplet.sh ./api ./web`  
4. `journalctl -u tradewithbeast-api -n 100` — look for `DEPLOYMENT CHECK PASS`  
5. `curl -fsS https://<hostname>/api/health`  
6. Admin login → `GET /api/admin/deployment-readiness`

---

## Restricted production rules

- **`LiveTrading__Enabled=false`** until documented live-money approval.
- Halt emergency gate before deploys that touch order execution code.
- **Single instance** — do not share SQLite across multiple hosts.
- No automatic real-money orders in restricted mode.

---

## Database backup and recovery

- **Automatic:** `ProductionSafety:DatabaseBackupEnabled` (in-app, 24h default)  
- **Manual:** `backup-sqlite.sh` (exits non-zero on failure)  
- **Pre-deploy:** copy existing `trade.sqlite` before first production start  
- **Restore:** stop API → `cp -a backup.sqlite /var/lib/tradewithbeast/trade.sqlite` → `chown twb:twb` → start API (see `rollback.sh`)

---

## Rollback

`rollback.sh /path/to/previous/api [/path/to/previous/web]`

Optional: `RESTORE_DB=/var/lib/tradewithbeast/backups/trade-<stamp>.sqlite`

---

## Firewall and monitoring

- [`FIREWALL.md`](./FIREWALL.md)  
- [`EXTERNAL-UPTIME-MONITORING.md`](./EXTERNAL-UPTIME-MONITORING.md)
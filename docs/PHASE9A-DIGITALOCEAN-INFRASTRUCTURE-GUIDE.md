# Phase 9A — DigitalOcean infrastructure provisioning guide

**Scope:** Single-host restricted production (`LiveTrading__Enabled=false`).  
**No droplet is provisioned by this document alone** — follow these steps in the DigitalOcean control panel and on the server.

---

## 1. DigitalOcean account

**Dashboard**

1. Go to [https://cloud.digitalocean.com](https://cloud.digitalocean.com) and sign up or sign in.
2. Add a payment method if prompted.
3. (Recommended) Enable **account 2FA**.

---

## 2. Create Ubuntu 24.04 LTS Droplet

**Dashboard:** Create → Droplets

| Setting | Recommended value |
|---------|-------------------|
| Image | Ubuntu 24.04 (LTS) x64 |
| Plan | Basic — **4 vCPU / 8 GB RAM** (or Dedicated if budget allows) |
| Datacenter | Nearest to your users (e.g. BLR1 for India) |
| Authentication | **SSH key** (see §4) — avoid password-only |
| Hostname | e.g. `twb-prod-01` |

Create the droplet. Record the **public IPv4** from the dashboard (do not commit it to git unless you use a private ops repo).

---

## 3. SSH public-key authentication

**Workstation (Windows PowerShell)**

```powershell
# If you do not have a key yet:
ssh-keygen -t ed25519 -C "twb-prod-deploy" -f "$env:USERPROFILE\.ssh\twb_prod_ed25519"
```

**Dashboard:** Account → Security → SSH Keys → Add → paste contents of `twb_prod_ed25519.pub`.

**Workstation:** After droplet is created:

```powershell
deploy\digitalocean\verify-ssh.ps1 -Server <DROPLET_IP> -User root -IdentityFile "$env:USERPROFILE\.ssh\twb_prod_ed25519"
```

First connection may require accepting the host key in an interactive `ssh` session (verify-ssh does **not** disable host-key checking).

---

## 4. Block storage volume

**Dashboard:** Volumes → Create Volume

| Setting | Value |
|---------|--------|
| Size | 50 GB+ (SQLite + backups) |
| Region | **Same as droplet** |
| Attach to | Your droplet |

**On the server (SSH)**

Identify device (often `/dev/disk/by-id/scsi-0DO_Volume_*`):

```bash
ls -l /dev/disk/by-id/ | grep DO_Volume
```

**Never format automatically.** Use the safe provision script (checks for existing filesystem signatures, requires explicit confirmation, prevents duplicate `fstab` entries):

```bash
export TWB_VOLUME_DEVICE=/dev/disk/by-id/scsi-0DO_Volume_<id>

# Existing filesystem on volume:
sudo bash deploy/digitalocean/provision-persistent-volume.sh --mount-only

# Brand-new empty volume only (destructive):
export TWB_CONFIRM_FORMAT=YES
sudo bash deploy/digitalocean/provision-persistent-volume.sh --format-new
sudo bash deploy/digitalocean/provision-persistent-volume.sh --mount-only

# After reboot:
sudo bash deploy/digitalocean/provision-persistent-volume.sh --verify
```

Then:

```bash
sudo adduser --system --group twb
sudo chown -R twb:twb /var/lib/tradewithbeast
```

The API **refuses to start** in Production when `Infrastructure:RequirePersistentDataMount=true` and `/var/lib/tradewithbeast` is not a mount point (see `appsettings.Production.json`). systemd unit `RequiresMountsFor=/var/lib/tradewithbeast` blocks start until the volume is mounted.

---

## 5. DNS A record

**At your DNS provider** (not DigitalOcean unless you use DO DNS):

| Type | Name | Value |
|------|------|--------|
| A | `app` or `@ | `<DROPLET_PUBLIC_IPV4>` |

Propagation may take minutes to hours. Verify from workstation:

```bash
dig +short your-production-hostname.example
```

Set `TWB_DOMAIN` when running `verify-infrastructure.sh` on the server.

---

## 6. Install packages (on server)

```bash
sudo apt-get update
sudo apt-get install -y nginx certbot python3-certbot-nginx ufw sqlite3 rsync curl
```

### .NET 10 LTS ASP.NET Core runtime

.NET 8 reached end of support on 2026-11-10; production artifacts target **net10.0**.

```bash
wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
sudo dpkg -i /tmp/packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y aspnetcore-runtime-10.0
dotnet --list-runtimes | grep 'AspNetCore.App 10'
```

---

## 7. Firewall

**Dashboard (optional):** Networking → Firewalls — allow 22, 80, 443 inbound; attach to droplet.

**On server:**

```bash
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow OpenSSH
sudo ufw allow 'Nginx Full'
sudo ufw enable
```

See [`deploy/digitalocean/FIREWALL.md`](../deploy/digitalocean/FIREWALL.md).

---

## 8. Application layout (on server)

```bash
sudo mkdir -p /etc/tradewithbeast /opt/tradewithbeast/api /var/www/tradewithbeast
sudo cp /path/to/repo/deploy/digitalocean/tradewithbeast-api.service /etc/systemd/system/
sudo cp /path/to/repo/deploy/digitalocean/nginx-tradewithbeast.conf /etc/nginx/sites-available/tradewithbeast
# Edit server_name and SSL paths in nginx config to your real domain
sudo ln -sf /etc/nginx/sites-available/tradewithbeast /etc/nginx/sites-enabled/
sudo nginx -t
```

---

## 9. Production secrets

Copy template and edit **on server only**:

```bash
sudo cp deploy/digitalocean/env.template /etc/tradewithbeast/environment
sudo chmod 600 /etc/tradewithbeast/environment
sudo nano /etc/tradewithbeast/environment
```

See [`docs/PHASE9A-PRODUCTION-SECRETS-GUIDE.md`](./PHASE9A-PRODUCTION-SECRETS-GUIDE.md).

---

## 10. HTTPS (Certbot)

After DNS points to the droplet and nginx `server_name` is correct:

```bash
sudo certbot --nginx -d your-production-hostname.example
```

Re-run infrastructure check:

```bash
sudo TWB_DOMAIN=your-production-hostname.example TWB_CHECK_TLS=1 \
  bash deploy/digitalocean/verify-infrastructure.sh
```

---

## 11. Verify infrastructure (on server)

```bash
bash deploy/digitalocean/verify-infrastructure.sh
# After DNS/TLS:
TWB_DOMAIN=your-production-hostname.example TWB_CHECK_TLS=1 \
  TWB_ENV_FILE=/etc/tradewithbeast/environment \
  bash deploy/digitalocean/verify-infrastructure.sh
```

---

## 12. Deploy application (after preflight on workstation)

**Workstation**

```powershell
# Git Bash:
bash deploy/digitalocean/preflight.sh
.\deploy\digitalocean\deploy-release.ps1   # if rebuilding artifacts
.\deploy\digitalocean\upload-release.ps1 -Server <DROPLET_IP> -IdentityFile $env:USERPROFILE\.ssh\twb_prod_ed25519
```

Upload includes `artifacts/api`, `artifacts/web`, and `artifacts/bundle/` (scripts, nginx, systemd unit, manifest).

**Server**

```bash
cd /tmp/twb-release
test -f ./deploy-on-droplet.sh || { echo "missing deploy script"; exit 1; }
bash deploy-on-droplet.sh ./artifacts/api ./artifacts/web
# First install: copy unit + nginx from bundle, set hostname, certbot
curl -fsS http://127.0.0.1:8080/api/health
curl -fsS https://your-production-hostname.example/api/health
```

Full sequence: [`deploy/digitalocean/PRODUCTION-RUNBOOK.md`](../deploy/digitalocean/PRODUCTION-RUNBOOK.md).
# Phase 9A — Production secrets guide

**Never commit real values to git.** Store secrets only in `/etc/tradewithbeast/environment` on the server (mode **600**, owned by root).

Template: [`deploy/digitalocean/env.template`](../deploy/digitalocean/env.template)

---

## Required variables

| Variable | Purpose |
|----------|---------|
| `SystemIdentity__AdminPassword` | Bootstrap admin login; Production startup **fails** if weak/default |
| `Security__JwtSigningKey` | Session/JWT signing — long random string |
| `Security__DataEncryptionKey` | Application data encryption — separate long random string |
| `LiveTrading__Enabled` | Must remain **`false`** for restricted production |
| `Cors__AllowedOrigins__0` | Exact HTTPS origin of the Angular site (no trailing slash) |
| `Infrastructure__PublicApiBaseUrl` | Public site base URL (`https://your-host`) |
| `Database__SqlitePath` | `/var/lib/tradewithbeast/trade.sqlite` |
| `ProductionSafety__DatabaseBackupDirectory` | `/var/lib/tradewithbeast/backups` |
| `ASPNETCORE_URLS` | `http://127.0.0.1:8080` — API listens on loopback only (nginx terminates TLS) |
| `Infrastructure__PersistentDataRoot` | Set in `appsettings.Production.json` (`/var/lib/tradewithbeast`); mount required before API start |

---

## How to generate secrets (on a secure machine)

Use a password manager or OS CSPRNG. **Do not paste generated values into chat, tickets, or git.**

### Linux / macOS / WSL

```bash
# Admin password (store in password manager; type into environment file manually)
openssl rand -base64 24

# JWT signing key (64+ chars)
openssl rand -base64 48

# Data encryption key (separate from JWT)
openssl rand -base64 48
```

### Windows PowerShell

```powershell
# Random bytes as base64 (copy output only into server environment file)
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }))
```

Run **three separate** invocations for admin password, JWT key, and encryption key.

---

## Administrator password policy

- Minimum **16** characters; mix of upper, lower, digits, symbols.
- Not `ChangeMe-Admin-123!` or any value from documentation examples.
- Rotate via admin UI change-password after first login if desired.

---

## CORS and cookies

- Set `Cors__AllowedOrigins__0` to the **exact** browser origin users use (e.g. `https://app.example.com`).
- `appsettings.Production.json` sets `Security:CookieSecure=true` — requires **HTTPS** in production.
- Do not use `*` for CORS in production.

---

## Broker credentials (optional)

For **paper-only** restricted launch, leave `MStock__*` empty. Broker OTP login can be done later via UI.

When required:

- Set `MStock__ApiKey`, `MStock__Username`, `MStock__Password`, `MStock__TotpSecret`, etc. only in `/etc/tradewithbeast/environment`.
- Restrict SSH and file permissions; never log environment contents.

---

## Installing the environment file

```bash
sudo install -m 600 -o root -g root env.template /etc/tradewithbeast/environment
sudo nano /etc/tradewithbeast/environment   # replace all REPLACE_* placeholders
grep -q 'REPLACE_WITH' /etc/tradewithbeast/environment && echo "FIX PLACEHOLDERS" || echo "OK"
grep '^LiveTrading__Enabled=false' /etc/tradewithbeast/environment
```

`verify-infrastructure.sh` checks for placeholders and `LiveTrading__Enabled=false` when the file exists.

---

## What must not appear in git

- Admin password
- JWT or encryption keys
- Broker passwords, TOTP secrets, API tokens
- Production `environment` file copies
- Private SSH keys (`*.pem`, `id_*` without `.pub`)

`.gitignore` should exclude any local `environment` or `*.env` production copies if created on workstations.
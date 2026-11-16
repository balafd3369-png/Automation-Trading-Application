# DigitalOcean firewall (manual)

1. Create **Cloud Firewall** or use `ufw` on the droplet.
2. **Inbound allow:** `22/tcp` (SSH), `80/tcp`, `443/tcp` from trusted sources or `0.0.0.0/0` for web only.
3. **Deny:** direct access to `8080` (API) from the internet — nginx on 443 proxies locally.
4. **Outbound:** allow HTTPS for broker APIs, SMTP/Telegram if notifications enabled.

```bash
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow OpenSSH
sudo ufw allow 'Nginx Full'
sudo ufw enable
```
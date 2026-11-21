# Deploying the DeptChat web app (Hostinger VPS)

The web app runs on the **same VPS** as the API, at **https://departmentchat.tech**
(`www.` redirects there). It is a separate Docker stack, so deploying, updating or removing it
never rebuilds or restarts the API, database or Redis. The Android app keeps using
`https://179.197.255.148.sslip.io` unchanged.

```
browser ──https──> Caddy (deptchat_caddy) ──> web:8080 (deptchat_web) ──/api,/hubs,/uploads──> api:5051
phone   ──https──> Caddy ───────────────────────────────────────────────────────────────────> api:5051
```

| Piece | Where |
|---|---|
| Web source on the VPS | `/opt/deptchat-web` (git worktree of `/opt/deptchat`, branch `feature/web-app`) |
| Image / container | `deptchat-web` / `deptchat_web`, port 8080, internal only (no host port) |
| Network | joins the existing `deptchat_default` network |
| Compose file | `docker-compose.web.yml` (project name `deptchat-web`) |
| Public site | Caddy site block for `departmentchat.tech` in `/opt/deptchat/Caddyfile` |

The API branch checked out in `/opt/deptchat` is not changed. The only shared change is the
Caddy site block, applied with `caddy reload` (no Caddy restart, no API downtime).

## Before the first deployment

- The domain `departmentchat.tech` must be verified in hPanel (owner email link). DNS already
  points to the VPS: A `179.197.255.148`, AAAA `2a02:4780:5e:d72a::1`, CNAME `www` → `@`.
- `feature/web-app` must be pushed to GitHub (the VPS fetches from `origin`).
- Ports 80/443 are already open for Caddy; nothing else needs opening.
- The hub access checks (Phase 2, G1) are not in place yet. That is an existing API issue and
  the hub is already public for the Android app; the web app does not add a new way in, but keep
  the address to staff and test customers until Phase 2 is done.

## First deployment

Each step only with approval. Run on the VPS (`ssh deptchat-vps`).

### 1. Get the web code (does not touch `/opt/deptchat`'s working files)

```bash
cd /opt/deptchat
git fetch origin feature/web-app
git worktree add -b feature/web-app /opt/deptchat-web origin/feature/web-app
```

### 2. Build and start the web container

```bash
cd /opt/deptchat-web
docker compose -f docker-compose.web.yml build
docker compose -f docker-compose.web.yml up -d
docker logs --tail 20 deptchat_web
```

The log should show `DeptChat web forwarding /api, /hubs and /uploads to http://api:5051`.

Check it from inside the Caddy container (the site is not public yet):

```bash
docker exec deptchat_caddy wget -qO- http://web:8080/ | head -5
docker exec deptchat_caddy wget -qO- http://web:8080/api/app/version
```

### 3. Add the Caddy site

Back up the Caddyfile, then **append** the block. The Caddyfile is bind-mounted as a single
file, so do not edit it with `sed -i` or an editor that replaces the file: the container would
keep reading the old copy. `cat >>` writes into the same file.

```bash
mkdir -p /root/backups
cp /opt/deptchat/Caddyfile /root/backups/Caddyfile.pre-web
cat >> /opt/deptchat/Caddyfile <<'EOF'

# DeptChat web app (container deptchat_web, see docker-compose.web.yml).
departmentchat.tech {
    encode zstd gzip
    reverse_proxy web:8080
}

www.departmentchat.tech {
    redir https://departmentchat.tech{uri} permanent
}
EOF
md5sum /opt/deptchat/Caddyfile; docker exec deptchat_caddy md5sum /etc/caddy/Caddyfile
```

**The two checksums must match.** If an earlier `git checkout`/`pull` replaced the host file
after Caddy started, the container still reads the old one (this happened on the first
deployment, 2026-11-21). Then copy the new lines into the container's copy as well, instead of
restarting Caddy (a restart briefly takes HTTPS down for the phones):

```bash
tail -n +12 /opt/deptchat/Caddyfile | docker exec -i deptchat_caddy sh -c "cat >> /etc/caddy/Caddyfile"
```

(`+12` = first line after the original 11-line file; check the checksums match again.) Then:

```bash
docker exec deptchat_caddy caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
docker exec deptchat_caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile
```

The same applies to the rollback below: write the backup into both copies before reloading.

Caddy then gets the HTTPS certificate for both names by itself (watch with
`docker logs -f deptchat_caddy`, look for "certificate obtained successfully").

### 4. Verify

- https://departmentchat.tech opens the sign-in page with a valid certificate.
- https://www.departmentchat.tech redirects to https://departmentchat.tech.
- Sign in, open a chat, send a message, record a voice note (needs HTTPS, now available).
- Android app unaffected: https://179.197.255.148.sslip.io/api/app/version still answers, and a
  phone can send and receive.

## Updating the web app later

```bash
cd /opt/deptchat-web
git pull origin feature/web-app
docker compose -f docker-compose.web.yml build
docker compose -f docker-compose.web.yml up -d
```

Only the web container is replaced (a few seconds). Browsers pick up the new version on the
next reload (`index.html`, `deptchat.js` and `app.css` are served with `no-cache`).

## Rollback / removal

Nothing here touches the API, so rolling back never needs an API rollback.

```bash
cat /root/backups/Caddyfile.pre-web > /opt/deptchat/Caddyfile
docker exec -i deptchat_caddy sh -c "cat > /etc/caddy/Caddyfile" < /root/backups/Caddyfile.pre-web
docker exec deptchat_caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile
cd /opt/deptchat-web && docker compose -f docker-compose.web.yml down
```

To roll back to an earlier web version instead, check out that commit in `/opt/deptchat-web`
and rebuild as in "Updating".

## Notes

- `/opt/deptchat/Caddyfile` shows as locally modified in git until a branch with these site
  blocks (this branch) is merged and deployed to `/opt/deptchat`. Before a `git pull` there that
  changes the Caddyfile, keep the web blocks.
- Uploads through the web are capped at 25 MB by the app (the API allows 30 MB).
- Sessions created from the web record the web container's address as the IP, the same way
  phone sessions record Caddy's address today.

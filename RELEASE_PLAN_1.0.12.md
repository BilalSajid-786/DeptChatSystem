# Release Plan: DeptChat 1.0.12 (build 15)

**RELEASED 2026-11-23 (about 04:43-05:10 UTC).** All steps R1-R10 done and checked; tag `v1.0.12-b15`. Details in `REGRESSION_CHECKLIST.md` Part 2 §17a. Open follow-ups: section 5.

API + mobile app (forced update) + web app, released together.

**Branch rule (decided 2026-11-23):** `main` is the deployment branch from now on. All work is merged into `main`; the live server runs `main`, each release gets a tag (`v1.0.12-b15`). The test server follows `origin/main` as a detached checkout (it shares the live git repository, and a branch can only be checked out in one worktree).
No secrets in this file. Passwords, keys and hashes are typed by the owner on the day.

## 1. What goes live

| Part | From | Live today |
|---|---|---|
| API | `main` (feature branches merged 2026-11-23, 98ee023) | `release/1.0.11-b13` + S2 hotfix (image `deptchat-api:hotfix-s2` 9b15cd5d0bf2) |
| Mobile app | `main`, **1.0.12 build 15**, forced update | 1.0.11 build 13 |
| Web app (departmentchat.tech) | `main` (web code is in main now) | `feature/web-app` 55a628b (image 15511c734ef1) |

Main changes for customers and staff: personal message pins, clear chat, staff chat pins, disappearing messages, card-style chat menus, headset icon, password reset by admins/managers (the person stays signed in, BF-41), smaller pictures from the web, presence fixes (BF-38, BF-40), company management for the platform owner (web), Swagger off, chat hub access checks in log-only mode. Full list: `REGRESSION_CHECKLIST.md` BF-20 to BF-41 (BF-39, the 5-minute test option, was removed again) and Part 2 §18 to §28.

Database: the API adds the new tables/columns itself on start (idempotent bootstrap in `Program.cs`): pins, clear-chat columns, `Clients.DisappearingMessagesSeconds`, `Messages.ExpiresAt`. Nothing is removed, so the old API still runs on the new database (needed for rollback).

## 2. Decisions needed before release day

1. **Disappearing messages default: DECIDED 2026-11-23, keep 7 days.** Every existing company starts at 7 days (database default); new messages in all live chats disappear after 7 days unless an admin changes it. Tell customers/admins with the release (release notes). Messages sent before the release never disappear.
2. **Release window**: a quiet time; the API swap gives ~20 s of reconnecting, the web swap a few seconds.
3. **Who checks phones** on the day (one phone still on 1.0.11 for the forced-update test).

## 3. Preparation (no live changes)

| # | Step | Who |
|---|---|---|
| P1 | Team signs off **v42-test** on the test server (BF-38/BF-40 two-phone tests, BF-41 reset keeps the person signed in). | Team |
| P2 | Version commit on `main`: csproj `ApplicationDisplayVersion` 1.0.12 / `ApplicationVersion` 15; `appsettings.json` `AppVersion` Latest/Minimum 1.0.12 build 15, `ForceUpdate` true, new `ReleaseNotes`. | Claude, user approves the notes |
| P3 | Build the **release APK** from that branch without `DeptChatDiag`/`DeptChatTestLabel` (`dotnet publish ... -p:AndroidPackageFormat=apk -p:AndroidKeyStore=false`), signed with this PC's debug keystore. Record its sha256. | Claude |
| P4 | **Dry run on the test server** (already on `main`): temporarily remove the test compose `AppVersion__*` overrides, a phone with a 1.0.11 test APK must get the forced-update page; then put the overrides back. | Claude, team phone |
| P5 | **Prepare the rollback** (section 6): rollback API image and rollback APK, both built and stored before release day. | Claude |
| P6 | Fill the DevPlatformCompany values into `ops/new-client/add-client-and-admin.sql` (name DevPlatformCompany, code `DEVPLATFORM`, admin `Platform Admin` / `platform.admin`); the owner makes the hash with `make-password-hash.ps1` on the day. File is never committed. | Claude + owner |

### Preparation status (2026-11-23)

| Item | State |
|---|---|
| P1 sign-off | Owner tested BF-40/BF-41; no separate team round for v42. |
| P2 version + notes | Done: csproj 1.0.12/15 and `appsettings` AppVersion 1.0.12/15 forced, notes approved by the owner (2026-11-23), committed on `main`. |
| P3 release APK | `F:\Projects\DeptChatSystem\DeptChat-v1.0.12-b15.apk`, sha256 `8de14dea4a4fae0be00a540d4d5dff542c55e0fc64ea6230780c9795a53b5319`; no test features, new icon checked; uploaded to `/root/release-1012/`. |
| P4 dry run | Done on the test server: a build-13 app showed "Update Required 1.0.11 → 1.0.12"; release APK installed over it in place (versionCode 15). |
| P5 rollback | API image `deptchat-api:rollback-b16` (263d991a6699, branch `release/rollback-b16` 186cb45 = live code 179043c + AppVersion 1.0.11 build 16 forced, rollback APK inside), copy `/root/backups/deptchat-api-rollback-b16-263d991a6699.tar.gz`, worktree `/opt/deptchat-rollback-b16`. Rollback APK `DeptChat-v1.0.11-b16-ROLLBACK.apk` (branch `release/rollback-b16-app` 0f4d26e = live app 14d9326 as build 16), sha256 `049aba6d74e66eba5e534f2c06a4ef987da70caacee62e4aaa24a3b99277c92e`, also in `/root/release-1012/`. |
| P6 DevPlatformCompany | `ops/new-client/add-devplatform.sql` (git-ignored) prefilled; only the hash is missing. |

## 4. Release day (each step only after the owner's OK)

| # | Step | Check |
|---|---|---|
| R1 | **DB backup**: owner runs the backup script (types the sa password) → `/root/backups/DeptChatDb_pre_1012_<date>.bak`. | file size OK, "BACKUP OK" |
| R2 | **Keep the current state**: `docker tag deptchat-api:latest deptchat-api:rollback-pre-1012`, `docker tag deptchat-web:latest deptchat-web:rollback-pre-1012`, `.tar.gz` copies in `/root/backups`; copies of `docker-compose.yml`, `Caddyfile`, `.env`. | images listed |
| R3 | **Code**: in `/opt/deptchat`: `cp Caddyfile /root/backups/Caddyfile.pre-1012`; `git fetch`; `git checkout -- docker-compose.yml Caddyfile` (main's compose already has all live edits: 127.0.0.1 ports, JWT lines, plus the new `Platform__OwnerCompanyCode` and `Hub__EnforceAccessChecks` lines; main's Caddyfile has the web site; checked 2026-11-23); `git checkout main`; `cp /root/backups/Caddyfile.pre-1012 Caddyfile` (puts the test-server blocks back); check Caddyfile md5 host = container. | `git status`: only Caddyfile modified (test blocks) |
| R4 | **Settings** in `/opt/deptchat/.env`: add `PLATFORM_OWNER_COMPANY_CODE=DEVPLATFORM` and `HUB_ENFORCE_ACCESS_CHECKS=false`. No Swagger setting (stays off). | keys listed (values not printed) |
| R5 | **APK**: `cp /root/release-1012/DeptChat-v1.0.12-b15.apk /opt/deptchat/DeptChat.WebApi/wwwroot/apk/DeptChat.apk` (and as `DeptChat-v1.0.12-b15.apk`); it is baked into the image. | sha256 matches P3 |
| R6 | **API**: `docker compose build api` (~90 s, site still up), then `docker compose up -d --no-deps api` (~20 s reconnect). | logs: bootstrap without errors, "Now listening" |
| R7 | **API checks**: `/api/app/version` = 1.0.12 build 15; `/swagger` not found; login, send/receive, pins, clear chat; `/api/clients` 403 for Main Organization admin; Caddyfile md5 host = container. | all OK |
| R8 | **DevPlatformCompany**: owner runs the filled SQL script (hash typed by the owner), then `platform.admin` signs in on the web and sees Companies. | Companies page lists all companies |
| R9 | **Web**: `/opt/deptchat-web` `git fetch && git checkout --detach origin/main` (or the tag); build and restart `deptchat-web` (as in `DeptChat.Web/DEPLOYMENT.md`). | departmentchat.tech loads, login, chat, picture upload |
| R10 | **Phone**: a phone on 1.0.11 opens the app → forced-update page → installs 1.0.12 over it → stays signed in, chats load, messages both ways with the web. | OK |
| R11 | Disappearing messages: admin card shows **7 days** for live companies (decision 2.1, no action). | admin card shows 7 days |

If any check fails: **rollback (section 6) first, investigate afterwards.**

## 5. After the release

| When | Step |
|---|---|
| First hours | Watch API logs and problem reports (`deptchat_error_reports`), customer feedback. |
| Days 1-3 | `docker logs deptchat_api 2>&1 \| grep "HUB ACCESS would deny"`: no lines for real users in their own chats → with approval set `HUB_ENFORCE_ACCESS_CHECKS=true` and restart the API (§28). |
| After sign-off | Tag `v1.0.12-b15` on the released `main` commit and push the tag. Update `REGRESSION_CHECKLIST.md` (live versions, rollback images). |
| Owner | S5: check/change the live `manager` password (§29). |
| After 2026-12-22 | Remove `JWT_PREVIOUS_SECRET` from `/opt/deptchat/.env` and restart the API. |

## 6. Rollback (whole release, no diagnosis first)

The release changes API, app and web, so all three go back.

- **API**: `docker tag deptchat-api:rollback-b16 deptchat-api:latest && cd /opt/deptchat && docker compose up -d --no-deps api`. That image (prepared in P5) is the current live code (1.0.11 + S2) with `AppVersion` 1.0.11 build 16 forced and the rollback APK baked in. Plain `rollback-pre-1012` alone is not enough: phones already on build 15 would not be moved back.
- **App**: rollback APK = 1.0.11 app code as **build 16**, forced (phones on build 15 must get a higher build). Prepared and tested in P5.
- **Web**: `docker tag deptchat-web:rollback-pre-1012 deptchat-web:latest` and restart `deptchat-web`.
- **Database**: no restore needed (new tables/columns are ignored by the old code). Restore the R1 backup only if data is damaged; that loses messages sent after the backup.
- Settings added in R4 can stay (the old code ignores them).

Keep all existing rollback images and `.tar.gz` copies; never run `docker image prune -a` / `docker system prune -a` on the VPS.

# Release Plan: DeptChat 1.0.12 (build 15)

API + mobile app (forced update) + web app, released together.
No secrets in this file. Passwords, keys and hashes are typed by the owner on the day.

## 1. What goes live

| Part | From | Live today |
|---|---|---|
| API | `feature/pin-and-clear-chat` → release branch `release/1.0.12-b15` | `release/1.0.11-b13` + S2 hotfix (image `deptchat-api:hotfix-s2` 9b15cd5d0bf2) |
| Mobile app | same branch, **1.0.12 build 15**, forced update | 1.0.11 build 13 |
| Web app (departmentchat.tech) | `feature/web-pin-and-clear` | `feature/web-app` 55a628b (image 15511c734ef1) |

Main changes for customers and staff: personal message pins, clear chat, staff chat pins, disappearing messages, card-style chat menus, headset icon, password reset by admins/managers (the person stays signed in, BF-41), smaller pictures from the web, presence fixes (BF-38, BF-40), company management for the platform owner (web), Swagger off, chat hub access checks in log-only mode. Full list: `REGRESSION_CHECKLIST.md` BF-20 to BF-41 (BF-39, the 5-minute test option, was removed again) and Part 2 §18 to §28.

Database: the API adds the new tables/columns itself on start (idempotent bootstrap in `Program.cs`): pins, clear-chat columns, `Clients.DisappearingMessagesSeconds`, `Messages.ExpiresAt`. Nothing is removed, so the old API still runs on the new database (needed for rollback).

## 2. Decisions needed before release day

1. **Disappearing messages default.** Every existing company starts at **7 days** (database default). From the release on, new messages in all live chats disappear after 7 days unless an admin changes it. Options: keep 7 days (tell customers), or set live companies to **Off** right after the release with one SQL update (user runs it).
2. **Release window**: a quiet time; the API swap gives ~20 s of reconnecting, the web swap a few seconds.
3. **Who checks phones** on the day (one phone still on 1.0.11 for the forced-update test).

## 3. Preparation (no live changes)

| # | Step | Who |
|---|---|---|
| P1 | Team signs off **v42-test** on the test server (BF-38/BF-40 two-phone tests, BF-41 reset keeps the person signed in). | Team |
| P2 | Create `release/1.0.12-b15` from `feature/pin-and-clear-chat`: csproj `ApplicationDisplayVersion` 1.0.12 / `ApplicationVersion` 15; `appsettings.json` `AppVersion` Latest/Minimum 1.0.12 build 15, `ForceUpdate` true, new `ReleaseNotes`. | Claude, user approves the notes |
| P3 | Build the **release APK** from that branch without `DeptChatDiag`/`DeptChatTestLabel` (`dotnet publish ... -p:AndroidPackageFormat=apk -p:AndroidKeyStore=false`), signed with this PC's debug keystore. Record its sha256. | Claude |
| P4 | **Dry run on the test server**: deploy the release branch there, a phone with a 1.0.11 test APK must get the forced-update page; then put the test server back on the feature branch. | Claude, team phone |
| P5 | **Prepare the rollback** (section 6): rollback API image and rollback APK, both built and stored before release day. | Claude |
| P6 | Fill the DevPlatformCompany values into `ops/new-client/add-client-and-admin.sql` (name DevPlatformCompany, code `DEVPLATFORM`, admin `Platform Admin` / `platform.admin`); the owner makes the hash with `make-password-hash.ps1` on the day. File is never committed. | Claude + owner |

## 4. Release day (each step only after the owner's OK)

| # | Step | Check |
|---|---|---|
| R1 | **DB backup**: owner runs the backup script (types the sa password) → `/root/backups/DeptChatDb_pre_1012_<date>.bak`. | file size OK, "BACKUP OK" |
| R2 | **Keep the current state**: `docker tag deptchat-api:latest deptchat-api:rollback-pre-1012`, `docker tag deptchat-web:latest deptchat-web:rollback-pre-1012`, `.tar.gz` copies in `/root/backups`; copies of `docker-compose.yml`, `Caddyfile`, `.env`. | images listed |
| R3 | **Code**: in `/opt/deptchat` `git fetch`; `git checkout -- docker-compose.yml` (the branch version already has all live edits: 127.0.0.1 ports, JWT lines, plus the two new lines `Platform__OwnerCompanyCode` and `Hub__EnforceAccessChecks`; checked on 2026-11-23); `git checkout release/1.0.12-b15`. The Caddyfile edit (web site) stays as a local change. | `git status`: only Caddyfile modified |
| R4 | **Settings** in `/opt/deptchat/.env`: add `PLATFORM_OWNER_COMPANY_CODE=DEVPLATFORM` and `HUB_ENFORCE_ACCESS_CHECKS=false`. No Swagger setting (stays off). | keys listed (values not printed) |
| R5 | **APK**: copy the release APK to `DeptChat.WebApi/wwwroot/apk/DeptChat.apk` (it is baked into the image). | sha256 matches P3 |
| R6 | **API**: `docker compose build api` (~90 s, site still up), then `docker compose up -d --no-deps api` (~20 s reconnect). | logs: bootstrap without errors, "Now listening" |
| R7 | **API checks**: `/api/app/version` = 1.0.12 build 15; `/swagger` not found; login, send/receive, pins, clear chat; `/api/clients` 403 for Main Organization admin; Caddyfile md5 host = container. | all OK |
| R8 | **DevPlatformCompany**: owner runs the filled SQL script (hash typed by the owner), then `platform.admin` signs in on the web and sees Companies. | Companies page lists all companies |
| R9 | **Web**: `/opt/deptchat-web` `git fetch && git checkout feature/web-pin-and-clear`; build and restart `deptchat-web` (as in `DeptChat.Web/DEPLOYMENT.md`). | departmentchat.tech loads, login, chat, picture upload |
| R10 | **Phone**: a phone on 1.0.11 opens the app → forced-update page → installs 1.0.12 over it → stays signed in, chats load, messages both ways with the web. | OK |
| R11 | Decision 2.1 applied (keep 7 days or set Off). | admin card shows the value |

If any check fails: **rollback (section 6) first, investigate afterwards.**

## 5. After the release

| When | Step |
|---|---|
| First hours | Watch API logs and problem reports (`deptchat_error_reports`), customer feedback. |
| Days 1-3 | `docker logs deptchat_api 2>&1 \| grep "HUB ACCESS would deny"`: no lines for real users in their own chats → with approval set `HUB_ENFORCE_ACCESS_CHECKS=true` and restart the API (§28). |
| After sign-off | Merge `release/1.0.12-b15` into `main`, web branch into its main line; tag `v1.0.12-b15`. Update `REGRESSION_CHECKLIST.md` (live versions, rollback images). |
| Owner | S5: check/change the live `manager` password (§29). |
| After 2026-12-22 | Remove `JWT_PREVIOUS_SECRET` from `/opt/deptchat/.env` and restart the API. |

## 6. Rollback (whole release, no diagnosis first)

The release changes API, app and web, so all three go back.

- **API**: `docker tag deptchat-api:rollback-1012 deptchat-api:latest && cd /opt/deptchat && docker compose up -d --no-deps api`. That image (prepared in P5) is the current live code (1.0.11 + S2) with `AppVersion` set to the rollback APK and that APK baked in. Plain `rollback-pre-1012` alone is not enough: phones already on build 15 would not be moved back.
- **App**: rollback APK = 1.0.11 app code as **build 16**, forced (phones on build 15 must get a higher build). Prepared and tested in P5.
- **Web**: `docker tag deptchat-web:rollback-pre-1012 deptchat-web:latest` and restart `deptchat-web`.
- **Database**: no restore needed (new tables/columns are ignored by the old code). Restore the R1 backup only if data is damaged; that loses messages sent after the backup.
- Settings added in R4 can stay (the old code ignores them).

Keep all existing rollback images and `.tar.gz` copies; never run `docker image prune -a` / `docker system prune -a` on the VPS.

# Web app test checklist (round one)

For testing the web app (`DeptChat.Web`) together with the Android app. Run it against a
**local or staging backend, or a separate test company code** — never on live customers' data.
Phase 1 uses the backend unchanged; also run `REGRESSION_CHECKLIST.md` on the Android app to
confirm nothing changed for current users.

Result per line: ✅ pass · ❌ fail (note what happened) · — not tested.

## A. Already checked in the browser (local, 2026-11-20)

| # | Check | Result |
|---|---|---|
| A1 | Sign in (Admin, Manager, customer); wrong password shows the server message | ✅ |
| A2 | Create account with company code → signed in, session listed as "Web" | ✅ |
| A3 | Customer opens a department → own conversation is created / opened | ✅ |
| A4 | Live chat both ways; department and conversation previews update | ✅ |
| A5 | Ticks: grey ✓ when sent, blue ✓✓ when the other side opens the chat | ✅ |
| A6 | "typing…", "in chat", "online", "last seen" in the header | ✅ |
| A7 | Unread badges and "(n)" in the tab title; cleared when opened | ✅ |
| A8 | Photo and PDF upload; file names with `:` are made safe | ✅ |
| A9 | Delete (Admin any, Manager own) shows "This message was deleted" on both sides | ✅ (Admin) |
| A10 | Forward (Admin): department → customer; target customer receives it | ✅ |
| A11 | Block → customer can't send ("You have been blocked…"); Unblock restores | ✅ |
| A12 | Reset password → customer's open tab is signed out with the reason | ✅ |
| A13 | Manager: only "Add customer user"; admin pages show "no access" | ✅ |
| A14 | Accounts, Departments (add / delete / assign manager), My account pages load | ✅ |

## B. Web ↔ Android app — round one, 2026-11-20/21 (test APK 1.0.10 build 12 → test backend on dev PC)

| # | Check | Result |
|---|---|---|
| B1 | Customer on phone ↔ staff on web: messages arrive live both ways | ✅ |
| B2 | Ticks update on both sides (sent → delivered → read) | ✅ (web missed live ticks while its tab was hidden → fixed: refresh on rejoin) |
| B3 | Typing indicator shows on both sides | ✅ web → phone; phone → web not observed |
| B4 | **Voice note recorded on the web plays on the phone** (Chrome/Edge) | ✅ |
| B5 | **Voice note recorded on the phone plays on the web** | ✅ |
| B6 | Photo / PDF sent from web opens on phone, and the other way round | ✅ photo both ways, PDF web → phone; PDF phone → web not sent |
| B7 | Message deleted on web disappears on phone; forwarded message shows "Forwarded" | ✅ live when the phone is in the chat — see finding F1 |
| B8 | Web sign-out (menu › Sign out): phone stays signed in **and still gets push** | ✅ |
| B9 | Change password on web: web and phone are both signed out | ✅ |
| B10 | "Sign out all other devices" on web signs the phone out; web stays in | ✅ (phone signs out on its next contact; no instant signal) |
| B11 | Same user on web and phone at once: both receive messages | ✅ (no push while inside the chat — correct) |

### Findings (existing mobile/backend behaviour — recorded, not changed)

- **F1 — Deletes don't reach phones that weren't in the chat.** The hub only sends `MessageDeleted` to the room, and the phone app shows its saved copy and syncs only *newer* messages, so a message deleted while the phone was elsewhere keeps showing on that phone. Same when an Admin deletes from the phone app. Needs a mobile change (refresh on open) and/or backend change.
- **F2 — Stale "in chat" presence.** After the app is swiped away the server can still treat the phone as in the room: staff messages are stored as Read immediately and the web shows "in chat". Push is still sent. Backend presence behaviour (Phase 2 list).
- **F3 — In-app browser blocks the microphone** (Claude browser pane only); recording works in Chrome/Edge on `http://localhost` or HTTPS. Not an app issue; HTTPS is required when hosted.

## C. Web only (not yet checked)

| # | Check | Result |
|---|---|---|
| C1 | Voice: record, pause/preview, discard, send; playback speed 1× / 1.5× / 2× | |
| C2 | Firefox: recording is refused with a clear message; playback still works | |
| C3 | Drag-and-drop and paste an image into the chat | |
| C4 | Scroll up loads older messages (conversation with 30+ messages) | |
| C5 | Turn Wi-Fi off/on: "Reconnecting…" banner, queued message goes out, missed messages appear | |
| C6 | Reload with a pending message: it is re-sent once (no duplicate) | |
| C7 | Desktop notifications: allow in menu; message toast while tab hidden | |
| C8 | Phone-width browser: one pane at a time with back buttons | |
| C9 | Manager: delete own message allowed, colleague's message not offered | |
| C10 | Two tabs signed in, sign out in one → the other signs out too | |

## Known Phase 1 limits (not bugs)

- Signing out only clears the browser; the web session stays under Active sessions until revoked.
- Departments page can assign managers but can't list or remove assignments.
- No notifications when every DeptChat tab is closed.
- An Admin who forwards a message also gets a toast/unread for it (the hub sends forwards to the whole department).
- Blocking can take up to 10 minutes to sign out an already-open session (sending is blocked at once).

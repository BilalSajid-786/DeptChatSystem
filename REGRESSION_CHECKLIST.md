# Master Regression Checklist: Bug Fixes & Completed Enhancements

This document is the authoritative cross-checking reference for **DeptChat**. Any developer, agent (such as Claude Code), or QA engineer modifying the backend or mobile client **must** verify their changes against this list before committing to ensure that no previously fixed bug or existing enhancement is broken or regressed.

---

## Part 1: All Previously Fixed Bugs (Zero-Regression Checklist)

| ID | Module / Layer | Bug Description | Root Cause | Implemented Fix | What to Cross-Check (Do NOT Break) |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **BF-01** | Backend / SignalR | **Cross-Department & Cross-User Chat Leaks** | Messages weren't strictly partitioned per conversation ID in hub broadcasting. | Routed all messages and typing indicators strictly through SignalR group `Room_{conversationId}` and verified `ConversationId` matching in `ChatRoomViewModel`. | Ensure any new SignalR event sends to `Room_{id}` or `User_{id}`, never broadcast globally. |
| **BF-02** | Messaging / Receipts | **Stuck Delivery Ticks (`✓` / `✓✓`)** | Inconsistent receipt state updates and missing live updates when recipient enters chat. | Implemented 3-tier delivery lifecycle: Sent (`✓` Gray) $\rightarrow$ Delivered (`✓✓` Gray) $\rightarrow$ Read (`✓✓` Green/Blue). Emitted `MessagesDelivered` and `MessagesRead` SignalR events. | Ensure messages show `✓` on send, `✓✓` gray on app delivery, and turn green/blue when the recipient enters the room. |
| **BF-03** | Presence / WebSocket | **Online Status Race Condition & False Offline Drops** | Single-connection tracking dropped user status when user switched screens or closed one tab. | Migrated to multi-connection tracking in `IPresenceService` with a 90-second SignalR keepalive timeout. Header displays `"online"` or `"last seen today at h:mm tt"`. | Ensure top bar status doesn't flicker or show "offline" when navigating between conversations. |
| **BF-04** | Mobile UI (Android) | **Unwanted Black Navigation Bar at Top** | Default .NET MAUI shell navigation bar rendered as a black bar on Android. | Added `Shell.NavBarIsVisible="False"` to all XAML pages and created custom WhatsApp teal headers (`#008069`). | Ensure new XAML pages include `Shell.NavBarIsVisible="False"`. |
| **BF-05** | Mobile UX / Keyboard | **Chat Input Box Hidden Behind Keyboard** | Android software keyboard overlapped input fields and covered the latest messages. | Set `WindowSoftInputModeAdjust.Resize` in `MainActivity.cs`, structured page with Grid `RowDefinitions="Auto, *, Auto"`, and triggered `ScrollToLatest()` on size allocation. | Opening the keyboard in `ChatRoomPage` must shrink the message area and keep the input box and send button visible. |
| **BF-06** | Branding / Launcher | **App Icon Clipped / Wrong Launcher Title** | Launcher icon foreground extended outside Android 46% circular safe zone; app title was `DeptChat.Mobile`. | Centered chat glyph at 52% inside `Resources/appiconfg.svg` within safe zone; set `<ApplicationTitle>DeptChat</ApplicationTitle>` in `.csproj`. | App icon must be a clean green squircle/circle with centered white bubble, titled `DeptChat`. |
| **BF-07** | Mobile UI / Modal | **"New Chat" Modal Spontaneous Opening** | Modal visibility boolean was uninitialized or triggered on page load. | Initialized `_isSelectUserModalVisible = false` in `DepartmentConversationsViewModel.cs`; added tap-to-dismiss gesture on darkened background backdrop. | Entering a department conversation list must NEVER open any modal automatically. |
| **BF-08** | Data Quality / Queries | **Ghost 0-Message Conversations in Department List** | Department list displayed blank placeholder conversations where no messages had been exchanged. | Added `.Where(c => _dbContext.Messages.Any(m => m.ConversationId == c.Id))` filter in `ConversationsController.cs`. | Conversations with 0 messages must never appear in the department's active chat list. |
| **BF-09** | Mobile Local Cache | **Inverted Bubbles on Account Switch** | Shared local SQLite cache didn't distinguish between user IDs on the same device. | Purged outbox cache on `LogoutAsync()` and isolated SQLite database cache per `UserId`. | Logging out as Staff and logging in as Customer must never display inverted green/white chat bubbles. |
| **BF-10** | Multi-Staff Collaboration | **Colleague Messages Inverted / False Read Receipts** | Colleague staff messages rendered on the receiver side (left) and opening a room marked colleague messages as read. | Role-aware alignment: all staff messages render on the right (green) with colleague name above bubble; customer messages on left. Staff only marks customer messages as read. Customer presence is locked to `CreatedByUserId`. | Opening a chat room as Admin when another Manager texted must keep colleague's message on the right with their name, without marking it read until customer views it. |
| **BF-11** | Mobile Performance / Scroller | **Chat Scroll Hitching, Stutter & Freeze** | MAUI `CollectionView` gesture recognizers conflicted with Android native scrolling; layout thrashing occurred on cell reuse. | Removed blocking gesture recognizers, migrated touch detection to native RecyclerView `OnItemTouchListener`, expanded view pool, and enabled zero-traversal bindings. | Chat messages must scroll at a fluid 120fps with zero hitching or finger lag. |
| **BF-12** | Mobile Scroller | **Landing at Top Instead of Bottom on Chat Open** | Reverse layout offset jumps and premature `hasInitiallyScrolled` flag caused chats to open at message #1. | Configured `StackFromEnd` on native Android RecyclerView, disabled conflicting `KeepScrollOffset`, and enforced reliable post-layout bottom scroll. | Opening any chat room must ALWAYS land anchored at the bottom (newest message), never at the top. |
| **BF-13** | Mobile Scroller | **Scroll Hitch on Pagination / Older Message Load** | Inserting older messages at index 0 caused visible scroll jumps and layout re-measurements. | Suppressed layout measurements during pagination prepend and preserved scroll position anchoring when prepending older items. | Scrolling up to load older messages must smoothly prepend without jumping or snapping to bottom. |
| **BF-14** | Mobile APK Updates | **"App Not Installed: Conflicting Package" Error** | An ad-hoc custom release keystore changed the signing fingerprint between builds. | Reverted to standard default signing (`-p:AndroidKeyStore=false`) in `dotnet publish`. | In-place APK update over an existing installation must install cleanly without uninstalling the app. |
| **BF-15** | Mobile APK Distribution | **Stale APK Cached by Mobile Browsers** | Nginx/Caddy or browser cached the previous APK binary under `/api/app/download-apk`. | Enforced `Cache-Control: no-cache, no-store, must-revalidate`, `Pragma: no-cache`, `Expires: 0` headers, and appended dynamic version query string (`?b={build}`). | Downloading an update must always fetch the latest binary, never a cached older build. |
| **BF-16** | Android Compatibility | **App Crash on Budget 32-Bit Phones (Redmi A2+, Infinix)** | Missing 32-bit ARM native binaries in APK output. | Added `android-arm` (armeabi-v7a) alongside `android-arm64` and `android-x64` in `RuntimeIdentifiers` in `.csproj`. | APK must install and run smoothly on 32-bit budget Android devices. |
| **BF-17** | Mobile UI / Keyboard | **Password Fields Hidden Behind Keyboard in Modals** | Change Password and Admin Reset Password modals were fixed-height unscrollable cards. | Re-architected modal dialogs with responsive `Grid (Auto, *, Auto)` and wrapped form fields in `<ScrollView MaximumHeightRequest="480">`. | Tapping password fields inside modals must scroll smoothly so inputs and submit buttons stay visible above the software keyboard. |
| **BF-18** | Security / Auth | **Password Change Did Not Logout Other Devices** | JWT tokens remained valid for 30 days even after a user changed or reset their password. | Added `SecurityStamp` to `User` entity; embedded `security_stamp` claim in JWT; validated against `IMemoryCache` on every request; rotated stamp and revoked sessions on password change. | Changing a password on Device A must immediately invalidate tokens on Device B, Device C, etc. with `401 Unauthorized`. |
| **BF-19** | Docker / Server | **SQL Server Container Crashing on VPS Startup** | Linux file permission denied on mounted `/var/opt/mssql/data` volume. | Configured `user: "0:0"` (root permissions) in `docker-compose.yml` for `deptchat_sql`. | Docker Compose restart must bring up SQL Server with healthy database state. |
| **BF-20** | Mobile / Customer List | **Stale Customer List After Network Drop or Resume** *(released in 1.0.11 build 13)* | The department conversation list only refreshed on live SignalR events, so messages missed while offline, reconnecting or in the background never appeared until the screen was reopened. | `DepartmentConversationsViewModel` reloads on SignalR `OnReconnected`, on connectivity returning to Internet, and on `App.AppResumed`; plus a 45 s periodic refresh that runs only while the app is in the foreground (`App.IsInForeground`). All handlers are removed in `UnsubscribeFromEvents`. | After turning the internet off/on, reconnecting, or returning from background, the list must show new chats and last messages without reopening the screen. No duplicate rows, no reload loops, no refresh while in background. |
| **BF-21** | Mobile / Departments | **Old Deleted Departments Shown on Load Failure** *(released in 1.0.11 build 13)* | The `catch` in `DepartmentsViewModel.LoadDepartmentsAsync` called `LoadDefaultDepartments()`, which showed hardcoded seed departments (Complaints & Feedback, IT Support, Billing & Accounts) whenever the API call failed. | Error paths no longer use the hardcoded list. Empty list + failure shows "Couldn't load departments" with a **Try again** button; a failed refresh with an existing list shows an orange "Couldn't refresh. Showing the last list." banner. Added pull-to-refresh, and reloads on reconnect, connectivity return and app resume. One automatic retry after 2 s (not for 401). | A failed load must never show the seed departments. 401 must still log out. Logout clears the list and both error flags. `LoadDefaultDepartments()` is only for the designer constructor. |
| **BF-22** | Messaging / Receipts | **Sender's Tick Stuck on Single `✓` After Recipient Read It** *(released in 1.0.11 build 13)* | `MessagesRead` is only broadcast to `Room_{id}`, so a sender outside the chat missed it, and `SyncMissedMessagesAsync` only fetched messages newer than the highest known sequence, so ticks on already-loaded messages never refreshed. | `SyncMissedMessagesAsync` lowers the sync start to the oldest own outgoing message with `Status < Read` among the last 30 (`StaleTickCheckWindow`), so their status is re-fetched. Chat also syncs on `App.AppResumed`. Status only moves forward (`UpdateStatusVisuals` ignores downgrades). | Repro: Admin stays on the list → User sends a message (`✓`) → User presses Back → Admin opens the chat → User reopens the chat: must show blue `✓✓`. Re-sync must not duplicate messages, must not scroll to bottom (BF-12), and must keep `ClientGeneratedId` idempotency and pending-outbox protection. |
| **BF-23** | Mobile / Networking | **"Couldn't load departments" With Working Internet** *(released in 1.0.11 build 13)* | `FastHttpHandler` gave up connecting after 3 s (`ConnectTimeout`). On a fresh app start a slower phone/network needs longer for DNS (`sslip.io`) + TCP + TLS, so loads failed although the server was fine (seen as `net_http_connect_timedout` in a Redmi problem report). | `ConnectTimeout` raised to 10 s in `Services/FastHttpHandler.cs` (shared by all API calls). | Normal loads must stay fast. With no internet the error may take up to ~10 s to show; the loading state must stay visible until then. BF-21 retry/error UI must still work. |
| **BF-24** | Mobile / Voice Playback | **Possible Screen Freeze When Tapping Play on a Long Voice Message** *(released in 1.0.11 build 13)* | `TogglePlayVoiceAsync` decoded the message's base64 audio (`Convert.FromBase64String`) on the UI thread. | Decoding and writing the cache file now run in `Task.Run`. The pause icon shows immediately; a second tap, another message starting, or leaving the chat while preparing cancels cleanly (`_currentlyPlayingItem` check). | Tapping play on a 1 min+ received voice message must not freeze the screen. Only one voice message plays at a time. Leaving the chat stops playback. |
| **BF-25** | Mobile / Clipboard | **Copy Text Offered on Voice, Image and File Messages** *(released in 1.0.11 build 13)* | `CanCopySelectedMessage` only checked that `Content` was not empty, so non-text messages copied their raw marker (e.g. base64 audio); the menu preview could show the marker too. | Copy and the menu preview text use `item.IsTextMessage`. The "Copied" popup is replaced by Android's own confirmation (Android 13+) or a short toast (older versions). | Long-press shows Copy only on non-deleted text messages; copied text pastes correctly. Forward/Delete menu permissions are unchanged. |
| **BF-26** | Mobile / Departments | **Two "Session expired" Alerts and Duplicate Department Loads** *(released in 1.0.11 build 13; the bug was in build 12 and older)* | `DepartmentsViewModel` is transient and subscribes to SignalR / connectivity / resume events in its constructor without ever unsubscribing. After logout + login a new copy is created and the old one kept reacting: two alerts on a forced logout and the department list loading twice per event. | A static `_current` marks the copy in use (set in the constructor and whenever its page appears, `IsPageVisible = true`). `RequestDepartmentsReload`, `HandleForceLogout` and the shared-file handler return early when `!IsCurrent`. | One department load per event and one forced-logout alert, also after several logout/login cycles and while the user is inside a chat (the forced logout must still appear there). Sharing a picture into DeptChat still opens the share dialog. |

---

## Part 2: All Completed Enhancements & Features

### 1. Multi-Tenant Architecture & Data Isolation
- **Tenant Scoping (`ClientId`)**: Every query on users, departments, and conversations strictly filters by the caller's `client_id` claim.
- **Tenant Management**: Admins have full visibility within their tenant; cross-tenant data leaks are physically blocked at the database query level.

### 2. Role-Based Permissions & Manager Provisioning (Build 11)
- **Three Core Roles**: `Admin` (Role 3), `Manager` (Role 2), `User` (Role 1).
- **Manager User Registration**: Managers have an in-app "Add New User" interface to register client users for their organization.
- **Role Escalation Prevention**: Server-side check strictly enforces `if (User.IsInRole("Manager") && dto.Role != UserRole.User) return BadRequest(...)`.
- **Department Manager Assignment**: Departments with assigned managers are accessible only to those managers; departments with no assigned managers are accessible to all tenant managers.

### 3. Active Session Management & Global Multi-Device Logout (Build 12)
- **Device & Session Tracking**: Persists `UserSession` capturing device model (e.g., *Samsung Galaxy S24 Ultra*), platform (*Android*), device ID, IP address, and login time.
- **Session API Endpoints**:
  - `GET /api/auth/sessions`: Lists all active devices; flags calling device as `isCurrentSession = true`.
  - `POST /api/auth/sessions/{id}/revoke`: Instantly terminates a specific remote device.
  - `POST /api/auth/sessions/revoke-others`: Logs out all other devices while preserving the active session.
- **Real-Time SignalR `ForceLogout`**: Broadcasting to `Clients.Group($"User_{userId}")` forces connected client devices to clear credentials, show an alert, and redirect to `//LoginPage`.
- **Mobile Active Sessions Screen**: A WhatsApp-styled management UI accessible from the Profile menu displaying platform icons, device names, "THIS DEVICE" badges, and logout buttons.

### 4. Multi-Identifier Authentication
- **Login Options**: Users can sign in using **Username**, **Email Address**, or **Phone Number**.
- **Admin Provisioning**: Admins can register new users with a simple alphanumeric username, without requiring an email address or mobile phone number.

### 5. Media Attachments & Multimodal Messaging
- **Camera & Gallery**: In-app camera photo capture and gallery picker with image compression.
- **Full-Screen Image Viewer**: Dedicated `ImageViewerPage` with pinch-to-zoom and pan support.
- **Audio Voice Notes**: Push-to-record voice messaging with audio playback waveform and timer.
- **Documents & Files**: Support for uploading and viewing PDFs and general file attachments with file size indicators.
- **Quick Sticker**: 1-tap "Deposit Done" quick sticker action for staff workflows.

### 6. Message Management & Collaboration Tools
- **2-Step Cross-Department Message Forwarding**: Long-press message $\rightarrow$ Forward $\rightarrow$ select destination department and customer chat.
- **Message Deletion**: Users and staff can delete messages with role-appropriate permissions.
- **User Blocking by Admins**: Admins can block problematic customer accounts from messaging departments.
- **WhatsApp Date Pills & Badges**: Clean floating date separator pills (e.g., *TODAY*, *YESTERDAY*, *DD/MM/YYYY*) between message groups.
- **Haptic Feedback**: Subtle vibration on long-press message context menus.

### 7. In-App Force Update Gate & APK Hosting
- **Server Version API**: `/api/app/version` reporting `LatestVersion`, `LatestBuildNumber`, `MinimumRequiredBuildNumber`, and `ForceUpdate`.
- **Startup Version Gate**: `AppVersionService` verifies version on app launch; displays non-bypassable `ForceUpdatePage` if build is outdated.
- **Server APK Hosting**: API serves `/api/app/download-apk` directly from `/wwwroot/apk/DeptChat.apk` with cache-busting headers.

### 8. Live Presence & Real-Time Notifications
- **WhatsApp "Last Seen"**: Live top bar shows green dot + `"online"` or gray dot + `"last seen today at h:mm tt"`.
- **Cross-Department In-App Toasts**: Green toast notification at top of screen when a new message arrives in another department.
- **Firebase Push Notifications (FCM)**: Background push notifications configured via mounted `firebase-admin-key.json`.

### 9. Hostinger Production Infrastructure & Automated Deployment
- **Docker Compose Setup**: SQL Server 2022 Express, Redis 7, ASP.NET Core 10 Web API, and Caddy 2 reverse proxy.
- **Automated SSL/TLS**: Caddy automatically provisions and renews Let's Encrypt certificates for `179.197.255.148.sslip.io`.
- **Zero-Downtime API Rebuild**: `docker compose build api && docker compose up -d --no-deps api`.
- **One-Click Deploy Script**: `deploy_hostinger.py` streams APK via SFTP, pulls latest git branch, stages download files, rebuilds container, and verifies health.

### 10. Automatic Problem Reports (released in 1.0.11 build 13)
- **All builds, no popup**: the old "Share report" prompt of test builds is gone. Problems are collected and sent silently by `Services/FreezeReport.cs`; nothing is ever shown to the user.
- **What triggers a report** (`BuildReport`):
  - Android exit history (`ApplicationExitInfo`, Android 11+): freezes (ANR, with stack trace), crashes, native crashes, and "low memory" only while the app was on screen. Background clean-ups ("low memory" / "too many empty") are listed for context but never trigger a report. Already reported exits are skipped via `diag_last_reported_exit_ts_v2`.
  - App errors recorded by `Services/DiagLog.cs` (all builds; entries capped at 4000 chars, file `diag_errors.log` ~200 KB, network state per entry). Today only department-list load failures (`DepartmentsViewModel`) and crashes are recorded.
  - Crashes: `AppDomain.UnhandledException` and `AndroidEnvironment.UnhandledExceptionRaiser` (`MauiProgram.cs`) call `FreezeReport.RecordCrash`; the report is sent on the next app start.
- **Kind** (part of the file name on the server): `freeze` > `crash` > `network` (HttpRequestException, SocketException, timeouts, network None/Local) > `error`.
- **When it runs** (`CheckAndSendAsync`, never on the UI thread, one run at a time): app start (+4 s, `App.xaml.cs`), resume (+3 s, at most once per 5 min), after login and register (+3 s, `AuthService`).
- **Saved first, then sent**: each report is a JSON file in `AppDataDirectory/pending_error_reports` (newest 10 kept) and is deleted only after the server accepts it (2xx) or rejects it permanently (400/413). Not logged in, offline, 401, 404, 429 or server errors: kept and retried later.
- **Content**: report text (cut to 1.9M chars) plus app version, build, test label, manufacturer, model, Android version (API level), network type, RAM / free memory / 32- or 64-bit / ABI / low-RAM flag, and the time of the problem.
- **Channel**: `internal` for builds with `-p:DeptChatDiag=true` (or Debug), `live` for customer builds (section 12).
- **Verified**: 2026-11-21 airplane-mode test on a Samsung SM-A047F (Android 14, v26 test build) produced `internaltesterrors/2026-11-21/102613_network_buser_7aa78c0a.txt` on the live server.
- **Known gaps**: network errors outside the department list (chat send/load, SignalR, uploads) are not recorded yet; Android 10 and older have no freeze history (crashes are still reported); the "Occurred" time of error-only reports is when the report was built, not when the first error happened (exact times are inside the report).
- **Do not break**: no popup, toast or alert may ever appear for reports; nothing may block app start or the UI thread; never delete a pending report on network errors; login/register must still work when the report endpoint is missing (older server).

### 11. Voice Message Enhancements (released in 1.0.11 build 13)
- **WhatsApp-style voice bubble** (`VoiceMessageTemplate` in `ChatRoomPage.xaml`): round picture with the sender's initial and a mic badge (no profile photos exist), play/pause button, waveform bars, and one line with position/duration on the left and time + ticks on the right.
  - Waveform is drawn by one lightweight view (`Views/VoiceWaveformView.cs`, a `GraphicsView`): no child views, no allocations while drawing, `InputTransparent`. Played part uses the play-button colour.
  - **Real waveform** (`Services/VoiceWaveforms.cs`): bar heights are read from the audio with Android's decoder (`MediaExtractor` + `MediaCodec`, loudest sample per bar, 28 bars, 1 byte each). Until ready (or if reading fails) a placeholder pattern from a stable per-message seed (`WaveSeed`) is shown.
    - Worked out by `ChatRoomViewModel.ScheduleWaveforms`: one message at a time, newest first, in `Task.Run`, and **only while the list is still** (waits while scrolling, opening the chat, bulk sync or loading older messages). Stops when leaving the chat.
    - Results kept in memory and in `cache/voice_waveforms/` (one small file per message); cleared on logout (`AuthService`, like picture thumbnails, BF-09). Logout also deletes the voice audio files in the cache folder (`*.m4a`: played/analysed messages and own recordings) so the next user of the phone does not inherit them.
    - The audio file it creates (`cache/{ClientGeneratedId}.m4a`) is the same one playback uses; both write it via a temporary file + rename (`VoiceWaveforms.WriteVoiceAudioFile`) so neither reads a half-written file.
  - Seeking still uses the existing `Slider` (transparent track, thumb only) placed over the bars. No gesture recognizers were added (Part 3 item 5).
  - All parts have fixed sizes (row content 52 px) so the row height never changes between idle and playing (BF-11 / BF-12 / BF-13).
- **Playback speed 1x / 1.5x / 2x**: the speed button replaces the round picture while playing; tap cycles the speed. Remembered in `Preferences` (`voice_playback_speed`). Applied with Android `PlaybackParams` (`AndroidAudioService`). Recording preview always plays at 1x.
  - Progress bar and timer follow the player's real position (`GetCurrentPositionMs`), not a fixed 0.1 s tick, so they stay correct at 1.5x / 2x.
- **Message sent sound**: `Platforms/Android/Resources/raw/message_sent.wav` is an original sound generated for DeptChat (not copied from WhatsApp). Played via `SoundPool` when the server first confirms a message (Pending -> Sent in `HandleMessageAck`), only while the app is in the foreground, never in silent/vibrate mode, at most once per second (a burst of queued messages plays once). The sound loads in the background on chat open.
- **Do not break**: the `[voice:data:audio/m4a;base64,...:seconds]` content format (parsed by server and older apps), forwarding and deleting voice messages, BF-10 colleague names and alignment, BF-02/BF-22 tick behaviour (the sound never changes message status).

### 12. Test Builds vs Customer Builds
- Internal test APKs are built with `-p:DeptChatDiag=true -p:DeptChatTestLabel=vNN`:
  - `DeptChatDiag` only switches the report channel to `internal` (server folder `internaltesterrors`).
  - `DeptChatTestLabel` adds "· test vNN" to the version line on the Departments screen (`AssemblyMetadata`).
- Customer release APKs are built with **neither** flag: reports go to `live` (`livecustomererrors`), no test label.

### 13. Tapping a Message Notification Opens the Chat (released in 1.0.11 build 13)
- Mobile only: the server already sends `conversationId` / `departmentId` in every message push (`ChatHub.SendMessage`); no backend change.
- `MainActivity` (now `LaunchMode = SingleTop`) reads `conversationId` from the tapped notification: Android's own tray notification (app closed/background) and the app's own (`MyFirebaseMessagingService.SendNotification`, which now attaches it with a separate request code per notification). Each tap is used once; an activity restore never re-opens an old tap.
- `Services/NotificationNavigation.cs` remembers the tap and opens it when the app is ready: straight away if already open and logged in, otherwise when `DepartmentsPage` appears after auto-login / login. Looks the chat up via the existing `GET /api/conversations/{id}` (server checks tenant, own chat, manager access); not allowed / deleted / other account / offline -> stays on Departments.
- Same screens as opening by hand so Back works: staff `Departments > customer list > chat` (customer details incl. blocked flag from the department list); user `Departments > chat`. Already in that chat -> nothing happens.
- **Do not break**: sharing a picture/PDF into DeptChat (same `MainActivity` entry point), forced update gate (always wins), forced logout / revoked session (goes to login), BF-07 (no popup on the customer list), BF-09 (never open another account's chat), BF-12 (chat opens at newest message), the navigation fixes (no double loads when two screens open at once), duplicate-notification suppression while inside the chat.

### 14. New Line Button in the Chat Input (released in 1.0.11 build 13)
- The message input in `ChatRoomPage.xaml` is an `Editor` (was a one-line `Entry`) named `MessageEditor`: `AutoSize="TextChanges"`, at least 42 px, at most 120 px (about 5 lines, then it scrolls inside). The surrounding pill uses `MinimumHeightRequest="48"` so it grows upward.
- A **↵ button** (between the text box and 📎) calls `OnNewLineClicked` (`ChatRoomPage.xaml.cs`): inserts a line break at the cursor or replaces the selected text, puts the cursor after it, and keeps the keyboard open. When the box is not focused, the line break is added at the end.
- The keyboard's Enter key also inserts a new line (normal `Editor` behaviour); sending is only by the send button.
- Line breaks are kept by the server (`ChatHub.SendMessage` only trims the start and end) and shown by chat bubbles (`WordWrap`); the conversation-list preview stays one line (`MaxLines="1"`). A message of only blank lines is not sent (`HasInputText` / `Trim`).
- **Do not break**: BF-05 (input and send button visible above the keyboard), BF-11/12/13 (no gesture recognizers on the chat list), the mic/send switch on `HasInputText`, image captions (`InputMessage` is used as the caption).

### 15. App Version on the Departments Screen (released in 1.0.11 build 13)
- `DepartmentsPage.xaml` shows `DepartmentsViewModel.AppVersionText`: "v1.0.11 (build 13)", plus "· test vNN" on test builds (section 12). Used by testers and support to see which build a phone runs.

### 16. Faster Lists and Smoother Chat Scrolling (released in 1.0.11 build 13)
- Department -> customer list: SignalR events only while the page is visible; duplicate loads skipped and event reloads debounced per department; server data merged into existing rows (no redraw when nothing changed); per-user/department cache (`ConversationListCache`, cleared on logout); page shown first, rows after the slide-in.
- Back to Departments: reload after the back animation; spinner only when the list is empty; never another user's list; background reloads only while the screen is visible.
- Chat screen: older messages load ~10 rows before the top, parsed off the UI thread, without interrupting a scroll; separate row templates per message type (`ChatMessageTemplateSelector`); voice data kept out of hidden labels; pictures shown as on-device downscaled copies (`ChatImageThumbnails` / `ThumbnailMaker`, EXIF-aware, cleared on logout); smaller RecyclerView caches; Mono GC nursery 16 MB (`Platforms/Android/environment.txt`, kept LF by `.gitattributes`).
- Opening a chat: messages added in one batch with one scroll to the bottom; screen shown first, messages after the slide-in; first page 25 messages.
- **Do not break**: BF-11, BF-12, BF-13, BF-09/BF-10 (per-user caches and bubble alignment), BF-20 refresh rules.

### 17. Error Report Endpoint (backend, live since 2026-11-21)
- `POST api/diagnostics/error-reports` (`DiagnosticsController`, `[Authorize]`), body `ErrorReportDto` (`DeptChat.Shared/DTOs/DiagnosticsDtos.cs`), returns `ErrorReportResultDto(ReportId)`.
- Saved as `/app/error-reports/{internaltesterrors|livecustomererrors}/yyyy-MM-dd/HHmmss_{kind}_{username}_{id}.txt` in the Docker volume `deptchat_error_reports` (outside `wwwroot`, never downloadable). Header: received/occurred time, user, username, role, tenant, app version and test label, device from the app and from the login session.
- Limits: 2,000,000 chars per report, 5 MB per request, 30 reports per user per hour (429). Unknown kinds become `other`; header values are single-line.
- **Do not break**: tenant-scoped user/session lookups, reports outside `wwwroot`, the volume in `docker-compose.yml`, the DTO parameter order (installed apps send it).

---

## Part 3: Mandatory Pre-Commit Checklist for Claude / Developers

Before committing any code change, verify all 10 items:
1. [ ] **Build Check**: `dotnet build DeptChat.WebApi/DeptChat.WebApi.csproj` must compile with **0 Errors**.
2. [ ] **Mobile Build**: `dotnet build DeptChat.Mobile/DeptChat.Mobile.csproj -f net10.0-android` must compile with **0 Errors**.
3. [ ] **Tenant Scoping**: All database queries involving tenant data must include `.Where(x => x.ClientId == callerClientId)`.
4. [ ] **Idempotent DB Migrations**: Any added entity or column must have an `IF NOT EXISTS ...` SQL block in `Program.cs`.
5. [ ] **Scroller Safety**: Never add blocking touch gesture recognizers or remove `StackFromEnd` from `ChatRoomPage.xaml`.
6. [ ] **Keystore Signing**: When publishing APKs, always use `-p:AndroidKeyStore=false` to avoid breaking in-place updates.
7. [ ] **Version Synchronization**: If bumping versions, update `DeptChat.Mobile.csproj` and `appsettings.json` together to matching build numbers.
8. [ ] **Test vs. Release APK**: internal test APKs use `-p:DeptChatDiag=true -p:DeptChatTestLabel=vNN` (reports to `internaltesterrors`, test label shown); customer release APKs are built with neither flag (reports to `livecustomererrors`).
9. [ ] **Release Notes**: update `AppVersion.ReleaseNotes` in `appsettings.json` with every release; it is shown on the forced-update page.
10. [ ] **Build Numbers**: build 14 is reserved for the prepared rollback of 1.0.11 (build 13); the next release must use build 15 or higher.

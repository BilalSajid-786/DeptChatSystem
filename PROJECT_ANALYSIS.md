# DeptChatSystem — Project Analysis

Technical documentation of the codebase as it exists on branch `feature/session-management-and-auth-fixes` (app version 1.0.10, build 12).

**Legend**
- ✅ **Verified** — confirmed by reading the source/config files cited.
- ❓ **Unknown** — cannot be determined from the repository.
- ⚠️ **Risk / debt** — verified problem or limitation.

No credentials, secrets, connection strings or key values are reproduced in this document. Where a file contains them, it is only noted that it does.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Technology Stack](#2-technology-stack)
3. [Architecture](#3-architecture)
4. [Folder & File Structure](#4-folder--file-structure)
5. [Backend (DeptChat.WebApi)](#5-backend-deptchatwebapi)
6. [Real-Time Hub (ChatHub)](#6-real-time-hub-chathub)
7. [Mobile Client (DeptChat.Mobile)](#7-mobile-client-deptchatmobile)
8. [Database](#8-database)
9. [Authentication, Authorization & Sessions](#9-authentication-authorization--sessions)
10. [Business Workflows](#10-business-workflows)
11. [External Integrations](#11-external-integrations)
12. [Development Setup & Commands](#12-development-setup--commands)
13. [Deployment](#13-deployment)
14. [Limitations & Technical Debt](#14-limitations--technical-debt)
15. [Security Findings](#15-security-findings)
16. [Spec vs. Implementation Gaps](#16-spec-vs-implementation-gaps)
17. [Unknowns](#17-unknowns)
18. [Recommended Reading Order](#18-recommended-reading-order)

---

## 1. Overview

✅ DeptChat is a **multi-tenant, department-based customer-support chat system** with a WhatsApp-style mobile UI.

- A **Client** (tenant / organization, identified by a unique company `Code`) owns **Departments** (e.g. Complaints, IT Support, Billing) and **Users**.
- A customer (`UserRole.User`) has **exactly one persistent conversation per department** — no tickets, no open/closed states ([DOMAIN_SPECIFICATION.md](DOMAIN_SPECIFICATION.md) §1; unique index in [ApplicationDbContext.cs](DeptChat.Infrastructure/Persistence/ApplicationDbContext.cs) lines 95–97).
- **Managers** answer conversations in departments they can access; **Admins** see all departments in their tenant.
- Customers must never see each other's conversations ("Golden Rule", [DOMAIN_SPECIFICATION.md](DOMAIN_SPECIFICATION.md) §3).

### Main features (verified in code)

| Feature | Where |
|---|---|
| Self-registration with company code; login by email, username or phone | [AuthController.cs](DeptChat.WebApi/Controllers/AuthController.cs) |
| Real-time text, image, document and voice messages | [ChatHub.cs](DeptChat.WebApi/Hubs/ChatHub.cs), [ChatRoomViewModel.cs](DeptChat.Mobile/ViewModels/ChatRoomViewModel.cs) |
| Delivery/read ticks, typing indicator, soft-delete, admin forward | [ChatHub.cs](DeptChat.WebApi/Hubs/ChatHub.cs) |
| Online / last-seen / in-room presence | `IPresenceService` + Redis/in-memory impls |
| FCM push notifications | [FirebasePushNotificationService.cs](DeptChat.Infrastructure/Services/FirebasePushNotificationService.cs), [MyFirebaseMessagingService.cs](DeptChat.Mobile/Platforms/Android/MyFirebaseMessagingService.cs) |
| Offline outbox + local message cache (SQLite) | [LocalOutboxService.cs](DeptChat.Mobile/Services/LocalOutboxService.cs) |
| Sequence-number delta sync + reverse pagination | [ConversationsController.cs](DeptChat.WebApi/Controllers/ConversationsController.cs) |
| Admin: departments, manager assignment, users, block/unblock, password reset, tenant block | Departments/Users/Clients controllers |
| Active-session management, global logout on password change/reset | [AuthController.cs](DeptChat.WebApi/Controllers/AuthController.cs), [UsersController.cs](DeptChat.WebApi/Controllers/UsersController.cs) |
| Forced app update gate + APK download | [AppVersionController.cs](DeptChat.WebApi/Controllers/AppVersionController.cs), [App.xaml.cs](DeptChat.Mobile/App.xaml.cs), [ForceUpdatePage.xaml.cs](DeptChat.Mobile/Views/ForceUpdatePage.xaml.cs) |
| Android share target (images, PDFs) | [MainActivity.cs](DeptChat.Mobile/Platforms/Android/MainActivity.cs), [IncomingShareService.cs](DeptChat.Mobile/Services/IncomingShareService.cs) |

---

## 2. Technology Stack

✅ Verified from `.csproj` files.

| Area | Technology | Source |
|---|---|---|
| Runtime | .NET 10 (`net10.0`); Shared also targets `net8.0` | all csproj |
| API | ASP.NET Core Web API, Swashbuckle 10.3.0 | [DeptChat.WebApi.csproj](DeptChat.WebApi/DeptChat.WebApi.csproj) |
| Real-time | ASP.NET Core SignalR, optional Redis backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`) | same |
| Auth | JWT Bearer (`Microsoft.AspNetCore.Authentication.JwtBearer`, `System.IdentityModel.Tokens.Jwt`) | WebApi + Application csproj |
| ORM / DB | EF Core 10.0.13, SQL Server provider | [DeptChat.Infrastructure.csproj](DeptChat.Infrastructure/DeptChat.Infrastructure.csproj) |
| Cache / presence | StackExchange.Redis 3.4.5; `IMemoryCache` | Infrastructure csproj, Program.cs |
| Push | FirebaseAdmin 3.7.0 (server), Xamarin.Firebase.Messaging (Android) | Infrastructure + Mobile csproj |
| Mobile | .NET MAUI 10.0.110, CommunityToolkit.Mvvm 8.4.0, SignalR client 10.0.1, sqlite-net-pcl | [DeptChat.Mobile.csproj](DeptChat.Mobile/DeptChat.Mobile.csproj) |
| Mobile targets | `net10.0-android` (min API 24) and `net10.0-windows10.0.19041.0` | Mobile csproj |
| Hosting | Docker (SDK/aspnet 10.0 images), docker-compose with SQL Server 2022, Redis 7, Caddy 2 | [Dockerfile](Dockerfile), [docker-compose.yml](docker-compose.yml), [Caddyfile](Caddyfile) |
| Dev tunnel | Cloudflare `cloudflared` (binary in gitignored `tools/`) | [start_tunnel.bat](start_tunnel.bat), [CLOUDFLARE_TUNNEL_GUIDE.md](CLOUDFLARE_TUNNEL_GUIDE.md) |

Note: iOS/MacCatalyst/Tizen platform folders exist under `DeptChat.Mobile/Platforms/` but those target frameworks are **not** listed in `<TargetFrameworks>`, so they are not built.

---

## 3. Architecture

### 3.1 Project dependency graph (✅ from ProjectReferences)

```
DeptChat.Shared  (DTOs, enums)            ◄── referenced by every project incl. Mobile & Simulator
   ▲
DeptChat.Domain  (entities)               ──► Shared
   ▲
DeptChat.Application (interfaces, JWT,    ──► Domain, Shared
                      password hasher)
   ▲
DeptChat.Infrastructure (DbContext,       ──► Application, Domain
                         Redis, FCM)
   ▲
DeptChat.WebApi (controllers, hub)        ──► Infrastructure, Application, Shared

DeptChat.Mobile (MAUI)                    ──► Shared only (talks to WebApi over HTTP + SignalR)
DeptChat.Simulator (console)              ──► Shared only (NOT in DeptChat.slnx)
```

### 3.2 Actual architectural style

- Folder layout follows **Clean Architecture** naming, but in practice it is a **thin layered design**:
  - `DeptChat.Application` contains only 4 interfaces ([Common/Interfaces](DeptChat.Application/Common/Interfaces)) and 2 implementations (JWT, PBKDF2). **No use cases, handlers, or business services.**
  - **All business logic lives in controllers and `ChatHub`**, which inject `ApplicationDbContext` directly. There is **no repository or service layer**.
- Abstraction via DI is used only for `IPresenceService`, `IPushNotificationService`, `IJwtTokenService`, `IPasswordHasher` (registrations in [Program.cs](DeptChat.WebApi/Program.cs) lines 174–184).
- Presence implementation is chosen at startup: Redis if the configured Redis connection succeeds, otherwise `InMemoryPresenceService` ([Program.cs](DeptChat.WebApi/Program.cs) lines 27–52, 176–183).

### 3.3 Communication between layers

| Channel | Used for |
|---|---|
| REST (`/api/...`) | Auth, department/conversation lists, paginated messages, delta sync, uploads, admin operations, presence offline/online, app version |
| SignalR (`/hubs/chat`) | Sending messages, acks, receipts, typing, presence, delete/forward, device-token registration |
| `IHubContext<ChatHub>` from controllers | Server-initiated events: `ForceLogout`, `DepartmentAdded`, `DepartmentDeleted`, `UserOnlineStatusChanged`, `UserPresenceChanged`, `UserPresenceState` |
| FCM | Wake/notify devices not connected |

**SignalR group naming** (✅ [ChatHub.cs](DeptChat.WebApi/Hubs/ChatHub.cs)):
- `User_{userId}` — personal events (toasts to customer, ForceLogout)
- `Client_{clientId}` — tenant-wide events (online status, department added/deleted)
- `Dept_{departmentId}` — staff in-app toasts (joined automatically on connect for Managers/Admins)
- `Room_{conversationId}` — live room traffic (joined via `EnterRoom`)

---

## 4. Folder & File Structure

Generated/ignored folders (`bin/`, `obj/`, `.vs/`, `tools/`, `*.apk`, `*.db`, uploads) are excluded — see [.gitignore](.gitignore).

```
/
├── DeptChat.slnx                  Solution (Application, Domain, Infrastructure, Mobile, Shared, WebApi)
├── DeptChat.Shared/               DTOs/ + Enums/ shared by server and app
├── DeptChat.Domain/Entities/      Client, User, Department, DepartmentManager,
│                                  Conversation, Message, DeviceToken, UserSession
├── DeptChat.Application/          Common/Interfaces/ (4) + Services/ (JwtTokenService, PasswordHasher)
├── DeptChat.Infrastructure/
│   ├── Persistence/ApplicationDbContext.cs   Fluent config, indexes, seed data
│   └── Services/                  RedisPresenceService, InMemoryPresenceService,
│                                  FirebasePushNotificationService
├── DeptChat.WebApi/
│   ├── Program.cs                 ENTRY POINT: DI, JWT, SignalR, DB bootstrap
│   ├── Controllers/               8 controllers
│   ├── Hubs/ChatHub.cs            Real-time logic
│   ├── appsettings.json           Config (contains secrets — see §15)
│   ├── firebase-admin-key.template.json   Template for FCM service account
│   └── wwwroot/uploads/           Uploaded files (served as static files)
├── DeptChat.Mobile/
│   ├── MauiProgram.cs             ENTRY POINT: DI registrations
│   ├── App.xaml(.cs), AppShell.xaml(.cs)   Window, update gate, Shell routes
│   ├── Services/                  AuthService, ChatApiService, SignalRService,
│   │                              LocalOutboxService, ServerConfigService, FastHttpHandler,
│   │                              AppVersionService, MediaHelper, IncomingShareService, audio
│   ├── ViewModels/                Login, Departments, DepartmentConversations, ChatRoom
│   ├── Views/                     XAML pages (+ ImageViewer, ForceUpdate)
│   ├── Resources/                 Styles.xaml, Colors.xaml, fonts, images, app icon
│   └── Platforms/Android/         MainActivity, FCM service, audio, keyboard observer,
│                                  AppPresenceService, manifest, network_security_config
├── DeptChat.Simulator/Program.cs  Console E2E smoke script (not in solution)
├── Dockerfile, docker-compose.yml, Caddyfile
├── start_backend_and_tunnel.bat, start_tunnel.bat
├── DOMAIN_SPECIFICATION.md        Business spec (its "status" section is outdated)
├── SESSION_HANDOVER.md            Prior session notes (partly outdated — see §14)
├── CLOUDFLARE_TUNNEL_GUIDE.md, SCROLLER_OPTIMIZATION_PLAN.md
└── MOBILE_UI_PREVIEW*.html        Static UI mockups
```

### File size hot spots (✅ line counts)

| File | Lines |
|---|---|
| [ChatRoomViewModel.cs](DeptChat.Mobile/ViewModels/ChatRoomViewModel.cs) | ~3,405 |
| [DepartmentsViewModel.cs](DeptChat.Mobile/ViewModels/DepartmentsViewModel.cs) | ~2,504 |
| [ChatRoomPage.xaml](DeptChat.Mobile/Views/ChatRoomPage.xaml) | ~2,068 |
| [DepartmentsPage.xaml](DeptChat.Mobile/Views/DepartmentsPage.xaml) | ~2,004 |
| [ChatHub.cs](DeptChat.WebApi/Hubs/ChatHub.cs) | ~915 |
| [AuthController.cs](DeptChat.WebApi/Controllers/AuthController.cs) | ~899 |

---

## 5. Backend (DeptChat.WebApi)

### 5.1 Startup pipeline ([Program.cs](DeptChat.WebApi/Program.cs))

1. `AddDbContext<ApplicationDbContext>` with SQL Server (`ConnectionStrings:DefaultConnection`) + `AddMemoryCache`.
2. Try Redis (`ConnectionStrings:Redis`, 1.5 s timeout); on success register `IConnectionMultiplexer` and the SignalR Redis backplane (channel prefix `DeptChat_SignalR`).
3. SignalR options: KeepAlive 15 s, ClientTimeout 90 s, max message 10 MB, `EnableDetailedErrors = true`.
4. JWT Bearer with issuer/audience/lifetime/signing-key validation. `OnMessageReceived` reads `access_token` query string for `/hubs` paths. `OnTokenValidated` checks security stamp + session (see §9).
5. Services: `PasswordHasher` (singleton), `JwtTokenService` (scoped), presence (singleton), FCM (singleton, eagerly resolved).
6. DB bootstrap: `EnsureCreated()` + raw idempotent SQL that adds `Users.SecurityStamp` and the `UserSessions` table; retries 15× with 3 s waits.
7. Middleware: Swagger (always on, `/swagger`) → `UseHttpsRedirection` → `UseStaticFiles` → `UseAuthentication` → `UseAuthorization` → `MapControllers` → `MapHub<ChatHub>("/hubs/chat")`.

✅ Not present: CORS, rate limiting, global exception handler, forwarded-headers middleware, health checks, EF migrations.

### 5.2 REST endpoints (✅ verified)

**AuthController** — `api/auth`
| Method | Route | Auth | Notes |
|---|---|---|---|
| POST | `register` | anon | Requires company code; forces `Role = User`; email or phone required; password ≥ 6 |
| POST | `login` | anon | Identifier matches Username, Email (lowercased) or Phone; creates `UserSession`; returns JWT |
| GET | `me`, `profile` | user | Profile |
| PUT | `profile` | user | FullName, PhoneNumber, Address |
| POST | `device-token` | user | Upserts FCM token for caller |
| POST | `logout` | user | Deletes **all** device tokens of the user (does not deactivate the session) |
| POST | `test-push` | user | Sends test FCM |
| POST | `change-password` | user | Rotates stamp, revokes all sessions, broadcasts `ForceLogout` |
| GET | `sessions` | user | Active sessions, flags current |
| POST | `sessions/{id}/revoke` | user | Revoke one |
| POST | `sessions/revoke-others` | user | Revoke all but current (no `ForceLogout` sent) |

**DepartmentsController** — `api/departments` (class `[Authorize]`)
| GET | `` | Departments visible to caller + unread counts + latest message preview |
|---|---|---|
| POST | `` | Admin: create department; broadcasts `DepartmentAdded` |
| POST | `{id}/managers` | Admin: assign Manager |
| DELETE | `{id}` | Admin: soft-delete (`IsActive=false`); broadcasts `DepartmentDeleted` |

**ConversationsController** — `api/conversations` (class `[Authorize]`)
| GET | `department/{departmentId}` | Customer: get-or-create own conversation. Staff: list conversations that have ≥1 message |
|---|---|---|
| GET | `{id}` | Conversation summary with access checks |
| GET | `{id}/messages?beforeSequenceNumber=&pageSize=` | Cursor pagination (pageSize clamped 5–100) → `PaginatedMessagesDto` |
| GET | `{id}/sync?sinceSequenceNumber=` | Delta sync → `ConversationSyncResponseDto` |
| POST | `register-device` | Device token upsert (reassigns token ownership to caller) |
| GET | `department/{departmentId}/available-users` | Staff: customers in tenant |
| POST | `start-with-user` | Staff: get-or-create conversation for a customer |

**UsersController** — `api/users` (class `[Authorize(Roles="Admin,Manager")]`)
| POST | `` | Create user; Managers may only create `User` role; Admin may create any role incl. Admin |
|---|---|---|
| GET | `` | Admin: all users in tenant |
| POST | `{id}/block` / `{id}/unblock` | Managers only on `User` targets; cannot block Admins |
| GET | `managers` | Admin |
| POST | `{id}/reset-password` | Admin; not for Admin targets; revokes sessions + `ForceLogout` |

**ClientsController** — `api/clients` (`[Authorize(Roles="Admin")]`): GET list of **all** tenants, `{id}/block`, `{id}/unblock`.

**AttachmentsController** — `POST api/attachments/upload`: 30 MB request limit, 25 MB file limit, extension allow-list (images, office docs, archives, audio). Saved to `wwwroot/uploads/yyyy-MM/{guid}_{name}{ext}`; returns relative URL.

**PresenceController** — `POST api/presence/offline`, `POST api/presence/online` (used by Android task-removed hook).

**AppVersionController** — anonymous `GET api/app/version` (from `AppVersion` config section) and `GET api/app/download-apk` (redirects to external `DownloadUrl` if configured, else serves `wwwroot/apk/DeptChat.apk` or `../DeptChat.apk`).

### 5.3 Shared controller patterns (✅)

- Each controller re-implements `GetUserId()`, `GetUserRole()`, `GetClientId()` from claims. `GetClientId()` **falls back to the default tenant GUID `00000000-0000-0000-0000-000000000001`** if the claim is missing.
- **Manager department-access rule** (repeated ~6 times, e.g. [ConversationsController.cs](DeptChat.WebApi/Controllers/ConversationsController.cs) lines 108–121): if a department has *any* `DepartmentManager` rows, only those managers may access it; if it has none, every manager in the tenant may.
- Errors are returned as `BadRequest/NotFound/Conflict(new { message = "..." })`; validation is manual `if` checks (no DataAnnotations/FluentValidation).
- `SanitizePreview` is duplicated in Departments and Conversations controllers; message preview building is duplicated in `ChatHub.SendMessage` and `ForwardMessage`.

---

## 6. Real-Time Hub (ChatHub)

✅ [ChatHub.cs](DeptChat.WebApi/Hubs/ChatHub.cs), `[Authorize]`, mapped at `/hubs/chat`.

| Method | Behavior |
|---|---|
| `OnConnectedAsync` | Joins `User_`, `Client_`; Managers join accessible `Dept_` groups, Admins all tenant `Dept_` groups; tracks connection; broadcasts `UserOnlineStatusChanged(true)` on first connection |
| `OnDisconnectedAsync` | On last connection: clears presence, persists `LastSeenAt`, broadcasts offline + room presence |
| `EnterRoom(convId)` | Joins `Room_`, sets presence `room:{id}`, marks other party's messages Read, sends presence state to caller |
| `LeaveRoom(convId)` | Clears presence, broadcasts, **then** removes from group (order matters) |
| `SendTyping` | `UserTyping` to others in room |
| `GetPresence` | Returns `UserPresenceDto` |
| `SendMessage(SendMessageDto)` | See §10.1 |
| `AcknowledgeDelivery(msgId)` | Sets Delivered; `MessageDelivered` |
| `MarkAsRead(convId, maxSeq)` | Sets Read; `MessagesRead` |
| `RegisterDeviceToken(token, platform)` | Upsert |
| `DeleteMessage(msgId)` | Customers denied; Managers only own messages; soft-delete, content replaced; `MessageDeleted` |
| `ForwardMessage(msgId, deptId, userId)` | Admin only; creates target conversation if needed |

Client events emitted: `ReceiveMessage`, `MessageAck`, `MessageDelivered`, `MessagesRead`, `InAppToast`, `UserTyping`, `UserPresenceChanged`, `UserPresenceState`, `DepartmentPresenceState`, `UserOnlineStatusChanged`, `MessageDeleted`, `MessageForwarded`, `ErrorMessage`, `ForceLogout`, `DepartmentAdded`, `DepartmentDeleted` (all subscribed in [SignalRService.cs](DeptChat.Mobile/Services/SignalRService.cs) lines 107–186).

---

## 7. Mobile Client (DeptChat.Mobile)

### 7.1 Composition ([MauiProgram.cs](DeptChat.Mobile/MauiProgram.cs))

- Singletons: `ServerConfigService`, one shared `HttpClient` (built from `FastHttpHandler`), `LocalOutboxService` (`deptchat_outbox.db3` in app data), `AuthService`, `ChatApiService`, `AppVersionService`, `SignalRService`, `IAudioService` (Android: `AndroidAudioService`; else `DefaultAudioService`).
- Transients: all ViewModels and Pages.

### 7.2 Navigation

- [AppShell.xaml](DeptChat.Mobile/AppShell.xaml): `LoginPage` and `DepartmentsPage` as ShellContent; Nav bar and flyout disabled.
- [AppShell.xaml.cs](DeptChat.Mobile/AppShell.xaml.cs): registers routes `DepartmentConversationsPage`, `ChatRoomPage`; starts on Departments if `auth_current_user` preference exists.
- Navigation via `Shell.Current.GoToAsync("ChatRoomPage?deptId=..&convId=..&userName=..&userId=..&isOnline=..&isBlocked=..")`; received with `[QueryProperty]` ([ChatRoomViewModel.cs](DeptChat.Mobile/ViewModels/ChatRoomViewModel.cs) lines 271–278).
- Logout navigates to `//LoginPage`.

### 7.3 Pages & ViewModels

| Page / VM | Responsibility |
|---|---|
| `LoginPage` / `LoginViewModel` | Login + registration |
| `DepartmentsPage` / `DepartmentsViewModel` | Department list, plus all admin/profile modals: create user, users/managers/clients lists, block, change/reset password, active sessions, share-to-department workflow, logout, `ForceLogout` handling |
| `DepartmentConversationsPage` / `...ViewModel` | Staff inbox per department with search filter |
| `ChatRoomPage` / `ChatRoomViewModel` | Messages, outbox, sync, pagination, presence, typing, attachments, voice record/preview/playback, delete/forward |
| `ImageViewerPage`, `ForceUpdatePage` | Image zoom; non-dismissible update gate |

### 7.4 State & data flow

- MVVM with CommunityToolkit (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`).
- No central store: singleton services + C# events on `SignalRService` (`OnMessageReceived`, `OnForceLogout`, …).
- `AuthService` keeps the full `AuthResponseDto` (incl. JWT) in `Preferences` under key `auth_current_user`.
- `ChatApiService` sets `DefaultRequestHeaders.Authorization` on the shared `HttpClient` before each call and adds `X-App-Version` / `X-App-Build` headers.
- `SignalRService`: `AccessTokenProvider`, custom WebSocket factory using `FastHttpHandler`, `WithAutomaticReconnect(new EndlessRetryPolicy())`, ServerTimeout 90 s, KeepAlive 15 s.
- `ServerConfigService` holds hard-coded server URLs (production host, a LAN IP, direct IP) and resets stale trycloudflare URLs saved in preferences.

### 7.5 Message content encoding (✅)

Message `Content` is a string with inline markers:
- `[image:{url}]` (placeholder `[image:uploading...]` while uploading)
- `[file:{url}:{name}]` (parsed by splitting on `:`)
- `[voice:data:audio/m4a;base64,{base64}:{duration}]` — **voice notes are embedded as base64 in the message text** ([ChatRoomViewModel.cs](DeptChat.Mobile/ViewModels/ChatRoomViewModel.cs) ~line 1815)

### 7.6 Android-specific

- [MainActivity.cs](DeptChat.Mobile/Platforms/Android/MainActivity.cs): `AdjustResize`, share intent filters (`image/*`, `application/pdf`, multiple images), FCM token retrieval.
- [AppPresenceService.cs](DeptChat.Mobile/Platforms/Android/AppPresenceService.cs): on task removal, synchronously calls `POST api/presence/offline`.
- [KeyboardHeightObserver.cs](DeptChat.Mobile/Platforms/Android/KeyboardHeightObserver.cs): manual keyboard inset for OEMs that ignore AdjustResize.
- [AndroidManifest.xml](DeptChat.Mobile/Platforms/Android/AndroidManifest.xml): `usesCleartextTraffic="true"`; permissions incl. RECORD_AUDIO, CAMERA, POST_NOTIFICATIONS.
- Application ID: `com.companyname.deptchat.mobile`.

---

## 8. Database

### 8.1 Technology

✅ SQL Server via EF Core. Schema is created with `Database.EnsureCreated()` and evolved with **hand-written idempotent SQL in [Program.cs](DeptChat.WebApi/Program.cs) lines 222–252**. **No EF migrations exist.**

### 8.2 Entities & relationships ([DeptChat.Domain/Entities](DeptChat.Domain/Entities), [ApplicationDbContext.cs](DeptChat.Infrastructure/Persistence/ApplicationDbContext.cs))

| Entity | Key fields | Relationships / constraints |
|---|---|---|
| `Client` | Id, Name, Code, IsActive, IsBlocked | Unique `Code`; 1-N Departments, Users, Conversations (Restrict) |
| `User` | Id, ClientId, FullName, Username?, Email?, PhoneNumber?, PasswordHash, Role, LastSeenAt, IsBlocked, SecurityStamp | Unique filtered Email; unique filtered PhoneNumber; unique (ClientId, Username) |
| `Department` | Id, ClientId, Name, Description, IsActive | FK Client (Restrict) |
| `DepartmentManager` | (DepartmentId, UserId) composite PK | Cascade both sides |
| `Conversation` | Id, ClientId, DepartmentId, CreatedByUserId, Status, LastMessagePreview, LastMessageAt | **Unique (DepartmentId, CreatedByUserId)**; FKs Restrict |
| `Message` | Id, ConversationId, SenderId, SenderRole, ClientGeneratedId, SequenceNumber, Content, Status, SentAt, IsDeleted, IsForwarded | **Unique (ConversationId, ClientGeneratedId)**; non-unique index (ConversationId, SequenceNumber); Cascade from Conversation |
| `DeviceToken` | Id, UserId, Token, Platform | No explicit config |
| `UserSession` | Id, UserId, DeviceName, DevicePlatform, DeviceId, IpAddress, IsActive, RevokedAt, RevokedReason | Index UserId; index (UserId, IsActive); Cascade |

Enums ([DeptChat.Shared/Enums](DeptChat.Shared/Enums)): `UserRole { User=1, Manager=2, Admin=3 }`, `MessageDeliveryStatus { Pending=1, Sent=2, Delivered=3, Read=4 }`, `ConversationStatus { Active=1, Archived=2 }` (Archived unused).

### 8.3 Seed data

✅ Fixed GUIDs in `OnModelCreating`: default client `MAIN` (`000…001`), one Admin (`111…`), one Manager (`222…`), three departments (`aaa…`, `bbb…`, `ccc…`), manager assigned to the Complaints department. Seed password hashes are present in code (with plaintext hints in comments — see §15).

### 8.4 Data access patterns

- LINQ directly in controllers/hub; `AsNoTracking` used in some reads.
- Delta sync: `SequenceNumber > since ORDER BY SequenceNumber`.
- Pagination: `SequenceNumber < before ORDER BY DESC TAKE pageSize+1` then reversed.
- Unread counts: `GroupBy ConversationId/DepartmentId`.
- N+1 presence lookups in loops (staff lists, conversation lists, available users).

### 8.5 Mobile local DB

✅ [LocalOutboxService.cs](DeptChat.Mobile/Services/LocalOutboxService.cs): single SQLite table `OutboxMessages` used as both send-outbox and message cache. Dropped and recreated when preference `outbox_cache_version` differs from `CurrentCacheVersion`. Key operations: `EnqueueAsync`, `GetPendingAsync`, `UpdateAckAsync`, `SaveServerMessageAsync`, `ClearConfirmedMessagesAsync` (keeps pending), `MigrateConversationIdAsync`, `ClearAllAsync` (on logout).

---

## 9. Authentication, Authorization & Sessions

### 9.1 Registration
`POST api/auth/register` → validates company code (active client) → uniqueness checks on email/phone → creates `User` with `Role = User` → returns JWT **without** `security_stamp` / `session_id` claims and **without creating a `UserSession`** ([AuthController.cs](DeptChat.WebApi/Controllers/AuthController.cs) line 214).

### 9.2 Login
`POST api/auth/login` → matches Username/Email (lowercase) or Phone **across all tenants** → PBKDF2 verify → rejects blocked user/client → creates `UserSession` → primes memory cache → JWT with claims:
`NameIdentifier`, `Name`, `Email`, `Role`, `client_id`, `client_name`, `security_stamp`, `session_id`, `department_id` (per assignment).

### 9.3 Token
HS256, issuer/audience from `JwtSettings`, default expiry 30 days (`ExpiryDays`) ([JwtTokenService.cs](DeptChat.Application/Services/JwtTokenService.cs)). No refresh tokens.

### 9.4 Per-request validation ([Program.cs](DeptChat.WebApi/Program.cs) lines 111–167)
- Load `SecurityStamp` from cache (10 min) or DB; on DB load, also rejects missing/blocked users.
- If token has a `security_stamp` claim and it differs → fail.
- If token has a `session_id` → check `UserSessions.IsActive` (cached 5 min).
- Tokens **without** these claims skip the checks.

### 9.5 Revocation
Password change / admin reset: new stamp, all sessions inactive, cache keys evicted, `ForceLogout` to `User_{id}`. Single-session revoke / revoke-others: session inactive + cache evict.

### 9.6 Password hashing
PBKDF2-SHA256, 100,000 iterations, 16-byte salt, 32-byte key, format `base64(salt):base64(hash)`, constant-time compare ([PasswordHasher.cs](DeptChat.Application/Services/PasswordHasher.cs)).

### 9.7 Roles & permissions (as implemented)

| Capability | User | Manager | Admin |
|---|---|---|---|
| See active departments in tenant | ✅ | assigned or unrestricted only | all |
| Own conversation per department | ✅ | — | — |
| Read/list department conversations (REST) | own only | per access rule | all in tenant |
| Start conversation with customer | — | ✅ | ✅ |
| Create users | — | `User` role only | any role |
| Block/unblock | — | `User` targets only | Users & Managers |
| Reset passwords | — | — | non-Admin targets |
| Create/delete departments, assign managers | — | — | ✅ |
| List/block tenants (`api/clients`) | — | — | ✅ (**all tenants**) |
| Delete messages | — | own only | any |
| Forward messages | — | — | ✅ |

---

## 10. Business Workflows

### 10.1 Send a message (customer → department)

1. `ChatRoomViewModel` creates a GUID `ClientGeneratedId`, saves `Pending` to SQLite outbox, renders optimistic bubble.
2. `SignalRService.SendMessageAsync` → hub `SendMessage(SendMessageDto)`.
3. Hub: reject blocked user / blocked client → ignore empty content → **idempotency check** (existing `ClientGeneratedId` ⇒ re-send `MessageAck`) → load conversation → `nextSeq = MAX(SequenceNumber)+1` → determine whether any staff is in the room (initial status `Read` vs `Sent`) → build preview → `SaveChangesAsync`.
4. Broadcast `ReceiveMessage` to `Room_{id}`, `MessageAck` to caller.
5. Customer sender: `InAppToast` to `Dept_{deptId}`, FCM to all assigned managers (or all tenant managers if none assigned) + tenant admins.
   Staff sender: `InAppToast` to `User_{customerId}`, FCM to customer's tokens.
6. Client updates the outbox row on ack; offline sends are flushed via `FlushOutboxAsync` on reconnect.

### 10.2 Open a chat room
Load latest 25 from SQLite → REST `GET messages` (or `sync`) → render → SignalR connect in background → `EnterRoom` (marks messages Read, returns presence) → scroll-up loads older pages via `beforeSequenceNumber`.

### 10.3 Staff inbox
`GET api/conversations/department/{id}` returns only conversations with ≥1 message, ordered by last activity, with unread counts and online flags.

### 10.4 Attachments
Upload via `POST api/attachments/upload` → returns URL → send message with `[image:url]` / `[file:url:name]`. Voice notes skip upload and embed base64.

### 10.5 Presence
Connection tracking in presence service; room presence string `room:{conversationId}`; Android task-removed hook posts `presence/offline`.

### 10.6 Forced update
On window create/resume, `App.CheckAndEnforceUpdateAsync` calls `api/app/version`; if required, replaces root page with `ForceUpdatePage`.

---

## 11. External Integrations

| Integration | Details | Status |
|---|---|---|
| Firebase Cloud Messaging | Server reads credential file path from `Firebase:CredentialPath` (default `firebase-admin-key.json`); runs in "simulation mode" (logs only) if missing. Android channel `deptchat_messages`, high priority | ✅ |
| Redis | Presence keys `presence:{id}`, `connections:{id}`, `online:{id}`, `lastseen:{id}`; SignalR backplane | ✅ optional |
| SQL Server | Primary store | ✅ required |
| Cloudflare Tunnel | Dev exposure via `cloudflared` | ✅ (binary not in repo) |
| Caddy | TLS reverse proxy to `api:5051` | ✅ |
| `google-services.json` | Present at [DeptChat.Mobile/Platforms/Android/google-services.json](DeptChat.Mobile/Platforms/Android/google-services.json) (Firebase client config) | ✅ |

---

## 12. Development Setup & Commands

### 12.1 Prerequisites
- .NET 10 SDK (verified locally: 10.0.402).
- .NET MAUI workload with Android support for the mobile app. ❓ The Android workload was **not** visible via `dotnet workload list` in the analysis environment (only `maui-windows` via Visual Studio), so the Android CLI build was not verified.
- SQL Server reachable via `ConnectionStrings:DefaultConnection`.
- Optional: Redis (`ConnectionStrings:Redis`), Firebase service-account JSON (see [firebase-admin-key.template.json](DeptChat.WebApi/firebase-admin-key.template.json)).

### 12.2 Configuration keys (values intentionally omitted)
`ConnectionStrings:DefaultConnection`, `ConnectionStrings:Redis`, `JwtSettings:{Secret,Issuer,Audience,ExpiryDays}`, `Firebase:CredentialPath`, `AppVersion:{LatestVersion,LatestBuildNumber,MinimumRequiredVersion,MinimumRequiredBuildNumber,ForceUpdate,DownloadUrl,ReleaseNotes}`. Environment-variable overrides use `__` (e.g. `ConnectionStrings__DefaultConnection`, as in docker-compose).

### 12.3 Commands

```bash
# Build backend (verified: succeeds, 0 errors, 11 warnings)
dotnet build DeptChat.WebApi/DeptChat.WebApi.csproj
```

```bash
# Run backend on http://0.0.0.0:5051 (launch profile "http" in Properties/launchSettings.json)
dotnet run --project DeptChat.WebApi --launch-profile http
```

```bash
# Full stack in Docker (SQL Server, Redis, API, Caddy)
docker compose up -d --build
```

```bash
# Release APK (command documented in SESSION_HANDOVER.md §6; not executed during analysis)
dotnet publish DeptChat.Mobile/DeptChat.Mobile.csproj -f net10.0-android -c Release -p:AndroidPackageFormat=apk -p:AndroidKeyStore=false
```

```bash
# E2E smoke script (requires running API on localhost:5051 and matching seeded accounts)
dotnet run --project DeptChat.Simulator
```

Windows helpers: [start_backend_and_tunnel.bat](start_backend_and_tunnel.bat) (runs the Debug exe + cloudflared), [start_tunnel.bat](start_tunnel.bat).

Swagger UI: `/swagger` (enabled in all environments).

### 12.4 Testing
✅ **No automated test projects exist.** The only verification tool is `DeptChat.Simulator`, which logs in with an account that is not part of the current seed data, so it will fail on a fresh database unless that account is created first. [DeptChat.WebApi.http](DeptChat.WebApi/DeptChat.WebApi.http) contains only the scaffold `/weatherforecast` request (endpoint does not exist).

### 12.5 Build warnings observed
- NU1903: vulnerable transitive `System.Security.Cryptography.Pkcs` 9.0.11 (Infrastructure).
- CS0618: obsolete Firebase APIs (`GoogleCredential.FromFile`, `MulticastMessage.Tokens`).
- CS8602 nullable warnings (e.g. ConversationsController line 146).

---

## 13. Deployment

✅ What is identifiable:
- [Dockerfile](Dockerfile): multi-stage build of `DeptChat.WebApi`, listens on 5051.
- [docker-compose.yml](docker-compose.yml): `sqlserver` (2022, Express), `redis` (7-alpine), `api` (Production env, uploads volume, Firebase key mounted read-only), `caddy` (80/443). SQL (1433) and Redis (6379) ports are published to the host.
- [Caddyfile](Caddyfile): reverse proxy for a single hard-coded host plus `:80`.
- APK distribution: server endpoint `api/app/download-apk`; version gating via `AppVersion` config.

❓ Unknown: CI/CD pipeline, target hosting provider, backup strategy, secret management in production, signing keystore for release APKs (documented command disables keystore signing).

---

## 14. Limitations & Technical Debt

| # | Issue | Evidence |
|---|---|---|
| D1 | No automated tests | No test projects in repo |
| D2 | No EF migrations; schema changes via raw SQL at startup | [Program.cs](DeptChat.WebApi/Program.cs) 210–270 |
| D3 | Business logic in controllers/hub; no service layer; duplicated helpers (`GetClientId`, access rule ×6, `SanitizePreview` ×2, preview builder ×2) | Controllers, ChatHub |
| D4 | Sequence numbers via `MAX+1` without transaction or unique index → concurrent sends may duplicate sequence numbers | [ChatHub.cs](DeptChat.WebApi/Hubs/ChatHub.cs) 419–424 |
| D5 | Concurrent duplicate `ClientGeneratedId` inserts would throw unhandled `DbUpdateException` | ChatHub `SendMessage` |
| D6 | Voice notes stored as base64 in `Message.Content` (DB and sync payload bloat; bounded by 10 MB SignalR limit) | ChatRoomViewModel ~1815 |
| D7 | Very large ViewModels/XAML (see §4) | line counts |
| D8 | N+1 presence lookups in loops | ConversationsController 138, 465; ChatHub 453, 247, 267 |
| D9 | Hard-coded URLs/IPs in mobile and server defaults; Cloudflare edge IP pinning | [ServerConfigService.cs](DeptChat.Mobile/Services/ServerConfigService.cs), [FastHttpHandler.cs](DeptChat.Mobile/Services/FastHttpHandler.cs), [AppVersionController.cs](DeptChat.WebApi/Controllers/AppVersionController.cs), [Caddyfile](Caddyfile) |
| D10 | Shared `HttpClient.DefaultRequestHeaders.Authorization` mutated per call (not thread-safe pattern) | [ChatApiService.cs](DeptChat.Mobile/Services/ChatApiService.cs) |
| D11 | FCM fire-and-forget via `Task.Run`; no retry/queue | ChatHub 563, 599 |
| D12 | Stale tokens never pruned from `DeviceTokens` on FCM failure | FirebasePushNotificationService |
| D13 | `IsAnyStaffActiveInRoomAsync` defined but unused; `ConversationStatus.Archived` unused; unused `Microsoft.AspNetCore.OpenApi` package | grep |
| D14 | `DeptChat.Simulator` not in solution; uses non-seeded account | [DeptChat.slnx](DeptChat.slnx), Simulator Program.cs |
| D15 | Docs outdated: `SESSION_HANDOVER.md` names a different branch, a non-existent `MessagesController`, and 3 s/6 s SignalR timeouts (code: 15 s/90 s); `DOMAIN_SPECIFICATION.md` §8 status predates implementation | docs vs code |
| D16 | `GetClientId()` silently defaults to the seed tenant when claim missing | all controllers |
| D17 | In-memory revocation cache is per-process; inconsistent with multi-instance Redis backplane | Program.cs, AuthController |
| D18 | Build warnings: vulnerable transitive package, obsolete Firebase APIs | §12.5 |

---

## 15. Security Findings

Ordered by severity. All verified in code.

1. **SignalR hub lacks conversation-membership authorization.** `EnterRoom`, `SendMessage`, `MarkAsRead`, `SendTyping`, `AcknowledgeDelivery`, `GetPresence` accept any conversation/user ID from any authenticated user. A user knowing a conversation GUID can join its room, receive live messages, and post into it. `DeleteMessage` lets any Admin delete messages in any tenant; `ForwardMessage` does not tenant-check the source message. REST endpoints *do* enforce access — the hub is inconsistent with them.
2. **Mobile TLS certificate validation disabled** (`RemoteCertificateValidationCallback => true`, [FastHttpHandler.cs](DeptChat.Mobile/Services/FastHttpHandler.cs) line ~282), plus cleartext traffic and user CAs trusted ([network_security_config.xml](DeptChat.Mobile/Platforms/Android/Resources/xml/network_security_config.xml)).
3. **Cross-tenant admin powers:** `ClientsController` lets any tenant's Admin list/block all tenants; Admins can create Admins via `api/users` (spec says Admins are seeded).
4. **Secrets committed to git:** `DeptChat.WebApi/appsettings.json` (DB connection string, Redis, JWT secret), `docker-compose.yml` (SQL SA password), plaintext test passwords in `SESSION_HANDOVER.md` and seed comments in `ApplicationDbContext.cs`, hard-coded fallback JWT secret in `Program.cs` and `JwtTokenService.cs`. Redis connection string is printed to console at startup. (Firebase admin key is gitignored and absent from history.)
5. **Infrastructure exposure:** compose publishes SQL Server 1433 and unauthenticated Redis 6379.
6. **Non-revocable tokens:** registration tokens lack stamp/session claims (30-day validity, survive password change). `logout` does not end the session.
7. **Blocking not immediate:** blocking a user/client does not revoke sessions or evict caches; `IsBlocked` only checked on cache miss (up to 10 min) — `SendMessage` does check it.
8. **Public uploads:** `wwwroot/uploads` served by `UseStaticFiles` without auth (GUID-named only).
9. **Hardening gaps:** no login rate limiting/lockout, 6-char password minimum, Swagger and `EnableDetailedErrors` enabled in production, JWT stored in `Preferences` (not `SecureStorage`), cross-tenant login lookup with `FirstOrDefault` despite per-tenant username uniqueness, `register-device` reassigns any existing token to the caller.

---

## 16. Spec vs. Implementation Gaps

Compared against [DOMAIN_SPECIFICATION.md](DOMAIN_SPECIFICATION.md):

| Spec item | Implementation |
|---|---|
| Strict room isolation (§3) | Enforced in REST, **not** in hub (§15.1) |
| Suppress push to other managers when one is in the room (§5 Rule 2) | Not implemented — FCM always sent to all staff tokens |
| 5-second ACK timeout → FCM fallback (§6.4) | Not implemented |
| Idempotency `UNIQUE(RoomId, ClientGeneratedId)` (§6.2) | ✅ Implemented |
| Delta sync by sequence (§6.3) | ✅ Implemented (`/sync?sinceSequenceNumber=`) |
| Admin seeded only (§2) | Admins can create Admins |
| Multi-tenancy | Not in spec; implemented via `Client` entity and company code |

---

## 17. Unknowns

- ❓ CI/CD, hosting provider, production secret management.
- ❓ Whether the Windows MAUI target is used in practice (it is in `TargetFrameworks`).
- ❓ iOS support status (platform folder exists, not targeted).
- ❓ Release signing process for APKs.
- ❓ Expected scale / whether multiple API instances are run (backplane support exists).
- ❓ Data retention / backup policy.
- ❓ Whether `DeptChat.Simulator` is still maintained.

---

## 18. Recommended Reading Order

1. [DOMAIN_SPECIFICATION.md](DOMAIN_SPECIFICATION.md) — intended behavior
2. [DeptChat.WebApi/Program.cs](DeptChat.WebApi/Program.cs) — composition, auth pipeline, DB bootstrap
3. [ApplicationDbContext.cs](DeptChat.Infrastructure/Persistence/ApplicationDbContext.cs) + [DeptChat.Domain/Entities](DeptChat.Domain/Entities)
4. [ChatHub.cs](DeptChat.WebApi/Hubs/ChatHub.cs) — core real-time workflow
5. [ConversationsController.cs](DeptChat.WebApi/Controllers/ConversationsController.cs), [AuthController.cs](DeptChat.WebApi/Controllers/AuthController.cs)
6. [DeptChat.Shared/DTOs](DeptChat.Shared/DTOs) — wire contract
7. [MauiProgram.cs](DeptChat.Mobile/MauiProgram.cs), [SignalRService.cs](DeptChat.Mobile/Services/SignalRService.cs), [LocalOutboxService.cs](DeptChat.Mobile/Services/LocalOutboxService.cs), [ChatApiService.cs](DeptChat.Mobile/Services/ChatApiService.cs)
8. [ChatRoomViewModel.cs](DeptChat.Mobile/ViewModels/ChatRoomViewModel.cs) — skim by section

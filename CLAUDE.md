# CLAUDE.md

Project instructions for Claude Code. Detailed documentation lives in [PROJECT_ANALYSIS.md](PROJECT_ANALYSIS.md) — read the relevant section before non-trivial work.

## Project

DeptChat: multi-tenant, department-based support chat (WhatsApp-style). Each customer has one persistent conversation per department; Managers answer conversations in departments they can access; Admins see everything in their tenant (`Client`). Business rules: [DOMAIN_SPECIFICATION.md](DOMAIN_SPECIFICATION.md).

Stack: .NET 10 · ASP.NET Core Web API + SignalR · EF Core + SQL Server · Redis (optional) · Firebase FCM · .NET MAUI (Android) with CommunityToolkit.Mvvm + SQLite.

## Layout

| Project | Contents |
|---|---|
| `DeptChat.Shared` | DTOs (`record`s) + enums shared by server **and** mobile app |
| `DeptChat.Domain/Entities` | EF entities |
| `DeptChat.Application` | Interfaces + JWT/password services only (no business logic) |
| `DeptChat.Infrastructure` | `ApplicationDbContext`, Redis/in-memory presence, FCM |
| `DeptChat.WebApi` | `Program.cs` (entry), `Controllers/`, `Hubs/ChatHub.cs` |
| `DeptChat.Mobile` | `MauiProgram.cs` (entry), `Services/`, `ViewModels/`, `Views/`, `Platforms/Android/` |
| `DeptChat.Simulator` | Console smoke script (not in `DeptChat.slnx`) |

Business logic lives directly in controllers and `ChatHub`, which use `ApplicationDbContext` directly. There is no repository/service layer — follow that pattern unless asked otherwise.

## Commands

```bash
dotnet build DeptChat.WebApi/DeptChat.WebApi.csproj
```

```bash
dotnet run --project DeptChat.WebApi --launch-profile http
```

```bash
docker compose up -d --build
```

```bash
dotnet publish DeptChat.Mobile/DeptChat.Mobile.csproj -f net10.0-android -c Release -p:AndroidPackageFormat=apk -p:AndroidKeyStore=false
```

- API listens on port 5051; Swagger at `/swagger`; hub at `/hubs/chat`.
- Mobile builds need the MAUI Android workload.
- **There are no automated tests.** Verify backend changes by building (must stay at 0 errors; existing warnings are known) and, where relevant, exercising endpoints via Swagger. Say clearly what was and was not verified.

## Inspect before modifying

| Area | Read first |
|---|---|
| Auth, JWT, sessions | `DeptChat.WebApi/Program.cs` (`OnTokenValidated`), `Controllers/AuthController.cs`, `DeptChat.Application/Services/JwtTokenService.cs` |
| Messaging / real-time | `DeptChat.WebApi/Hubs/ChatHub.cs`, `DeptChat.Mobile/Services/SignalRService.cs`, `ViewModels/ChatRoomViewModel.cs` |
| Conversations, sync, pagination | `Controllers/ConversationsController.cs`, `DeptChat.Mobile/Services/LocalOutboxService.cs` |
| Departments / manager access | `Controllers/DepartmentsController.cs`, `ConversationsController.cs` (access rule) |
| Users, blocking, password reset | `Controllers/UsersController.cs` |
| Entities / schema | `DeptChat.Domain/Entities/`, `DeptChat.Infrastructure/Persistence/ApplicationDbContext.cs`, `Program.cs` DB bootstrap |
| Presence | `IPresenceService` + both implementations in `DeptChat.Infrastructure/Services/` |
| Push | `FirebasePushNotificationService.cs`, `Platforms/Android/MyFirebaseMessagingService.cs` |
| Wire contract | `DeptChat.Shared/DTOs/` |
| Mobile DI, navigation, server URL | `MauiProgram.cs`, `AppShell.xaml.cs`, `Services/ServerConfigService.cs` |
| App version gate | `Controllers/AppVersionController.cs`, `App.xaml.cs`, `appsettings.json` `AppVersion`, Mobile csproj version |

## Rules

- **Make the smallest change that solves the task.** No unrequested refactors, renames, file splits, architecture changes or dependency upgrades — even in the very large files (`ChatRoomViewModel.cs`, `DepartmentsViewModel.cs`, `ChatHub.cs`). Mention improvements instead of doing them.
- **Preserve existing patterns**: claims helpers (`GetUserId`/`GetUserRole`/`GetClientId`), errors as `BadRequest/NotFound/Conflict(new { message = "..." })`, manual input validation, MVVM via `[ObservableProperty]`/`[RelayCommand]`, services as singletons and ViewModels/Pages as transients in `MauiProgram.cs`.
- **Tenant scoping**: every query on tenant data must filter by the caller's `client_id`.
- **Manager access rule**: a department with assigned managers is restricted to them; a department with none is open to all tenant managers. Reuse the existing check.
- **Hub methods**: any new or changed hub method must verify the caller may access the conversation (mirror `ConversationsController.GetMessages`). Existing hub methods currently lack this — see PROJECT_ANALYSIS.md §15.
- **DTOs are shared with installed apps**: add new record parameters as optional with defaults, at the end. Never reorder, rename or remove existing ones.
- **Schema changes**: there are no EF migrations. `EnsureCreated` does not alter existing DBs, so add an idempotent `IF NOT EXISTS ... ALTER/CREATE` block to the bootstrap in `Program.cs` alongside the entity/fluent change. Never change seed GUIDs.
- **Messaging invariants**: keep the `ClientGeneratedId` idempotency check, monotonic `SequenceNumber`, and pending-message protection in the outbox (`ClearConfirmedMessagesAsync`, `MigrateConversationIdAsync`).
- **Presence**: in `LeaveRoom`/`OnDisconnectedAsync`, broadcast before removing from the group.
- **Credential changes**: when changing passwords/sessions/stamps, evict `user_security_stamp_{userId}` and `user_session_active_{sessionId}` from `IMemoryCache`.
- **Message content markers** (`[image:..]`, `[file:url:name]`, `[voice:data:...]`) are parsed on both server and client — keep formats compatible.
- **Versions**: bump `ApplicationDisplayVersion`/`ApplicationVersion` in `DeptChat.Mobile.csproj` and `AppVersion` in `appsettings.json` together; wrong values lock users behind the force-update page.
- **Secrets**: never add, print or copy credentials, connection strings, JWT secrets or Firebase keys into code, docs, logs or commits. `appsettings.json`, `docker-compose.yml` and `SESSION_HANDOVER.md` already contain sensitive values — do not propagate them.
- `SESSION_HANDOVER.md` is partly outdated; trust the code over it.
- Commit only when asked.

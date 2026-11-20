# DeptChat web app

Browser version of DeptChat for customers, Managers and Admins. It uses the **existing**
API and SignalR hub unchanged (Phase 1: zero backend changes).

| Project | What it is |
|---|---|
| `DeptChat.Web.Client` | Blazor WebAssembly app (pages, chat, admin). References `DeptChat.Shared` only, like the Android app. |
| `DeptChat.Web` | Small host: serves the app and forwards `/api`, `/hubs` (incl. WebSockets) and `/uploads` to the backend with YARP, so the browser uses one origin and the API needs no CORS. |

## Run locally

1. Start the backend (`dotnet run --project DeptChat.WebApi --launch-profile http`, port 5051).
2. Start the web host:

```bash
dotnet run --project DeptChat.Web --launch-profile http
```

3. Open http://localhost:5080.

The backend address comes from `Backend:BaseUrl` in `DeptChat.Web/appsettings.json`
(default `http://localhost:5051`); override with the `Backend__BaseUrl` environment variable.

Tip: to test two accounts at once in one browser, open one at `http://localhost:5080`
and the other at `http://127.0.0.1:5080` (different origins keep separate sign-ins).

## Phase 1 limits (no backend changes)

- **Sign out** only clears this browser. It never calls `POST api/auth/logout` (that deletes the
  user's phone push tokens) and never revokes its own session (that sends ForceLogout to all
  the user's devices). The web session stays listed under Active sessions until revoked.
- **Register** signs in again right after registering, so the browser gets a real, revocable session.
- **Departments admin** can assign managers but can't list or remove assignments (no endpoint yet).
- **Notifications** only while a tab is open (in-app toasts + optional desktop notifications).
- **Voice notes** are recorded as AAC/MP4 (`[voice:data:audio/m4a;base64,...]`), the only format the
  Android app plays. Browsers that can't record MP4 (Firefox) can play voice notes but not record them.
- Keep the web app internal until the hub access checks (Phase 2, G1) are in place.

## Message markers (shared with server and Android app)

`[image:{url}]` / `[image:{url}:{caption}]`, `[file:{url}:{name}:{sizeBytes}]`,
`[voice:data:audio/m4a;base64,{base64}:{seconds}]` — see `DeptChat.Web.Client/Services/MessageContent.cs`.

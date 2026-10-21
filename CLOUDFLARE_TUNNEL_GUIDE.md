# Cloudflare Tunnel Setup & High-Performance Guide

This guide explains how to run Cloudflare Tunnel for **DeptChat** with maximum speed and zero lag, exactly as currently configured.

---

## 🚀 Why the Cloudflare Tunnel is Fast Now

If you previously experienced high latency, 20+ second delays, or `ObjectDisposed_Generic` errors, here is the technical breakdown of the fixes applied:

1. **Forced HTTP/2 Protocol (`--protocol http2`)**:
   - By default, Cloudflare tries to connect using **QUIC (UDP)**. On many ISPs and mobile carriers, international UDP traffic suffers from packet loss, aggressive throttling, or firewall drops, causing Cloudflare to stall and retry.
   - Forcing `--protocol http2` routes all tunnel traffic over rock-solid **TCP port 443**, enabling multiplexing and zero throttling.

2. **Decoupled HTTP Handler Lifecycle (Fixed `ObjectDisposed_Generic`)**:
   - Previously, a single HTTP handler was shared between the mobile app's REST `HttpClient` and the real-time `SignalRService`. When SignalR disconnected or refreshed, it disposed the shared handler, crashing all subsequent API requests.
   - Each service now gets an independent, dedicated handler instance.

3. **Full Native WebSockets (`WebSockets | LongPolling`)**:
   - SignalR is now configured to negotiate **WebSockets first**.
   - Cloudflare natively supports WebSockets. Instead of making hundreds of repetitive HTTP polling requests (each taking 500ms–2s), messages and delivery ticks stream instantly over a single persistent TCP connection.

4. **Native Android Network Engine (`AndroidMessageHandler`)**:
   - The mobile app now utilizes Android's native Java network stack (OkHttp). It maintains persistent HTTP/2 connection pools and SSL session resumption directly with Cloudflare edge servers.

---

## 🛠️ Step-by-Step: How to Run the Tunnel

### 1. Ensure the Backend WebApi is Running
Before launching the tunnel, ensure `DeptChat.WebApi` is running and listening on port `5051`.

In PowerShell:
```powershell
cd F:\Projects\DeptChatSystem\DeptChat.WebApi
dotnet run --urls "http://0.0.0.0:5051"
```
*(Or run `bin\Debug\net10.0\DeptChat.WebApi.exe --urls "http://0.0.0.0:5051"`)*

---

### 2. Launch the Cloudflare Tunnel
`cloudflared.exe` is already downloaded and located in `F:\Projects\DeptChatSystem\tools\cloudflared.exe`.

Run the following command in PowerShell:

```powershell
& "F:\Projects\DeptChatSystem\tools\cloudflared.exe" tunnel --protocol http2 --url http://127.0.0.1:5051
```

> **IMPORTANT**: Always include `--protocol http2`. Do not omit this flag, as it ensures optimal routing and avoids UDP packet drops.

---

### 3. Copy the Generated URL
When the command runs, Cloudflare will output a public HTTPS URL looking like:

```text
+--------------------------------------------------------------------------------------------+
|  Your quick Tunnel has been created! Visit it at (it may take some time to be reachable):  |
|  https://your-random-subdomain.trycloudflare.com                                           |
+--------------------------------------------------------------------------------------------+
```

Copy the `https://...trycloudflare.com` address.

---

### 4. Connect from the Mobile App

1. Open the **DeptChat** mobile app on your Android phone.
2. On the **Login Screen**:
   - If using the active pre-configured tunnel, tap the **`[☁️ Cloudflare]`** button under **ACTIVE BACKEND CONNECTION**.
   - If you launched a new tunnel with a new URL, paste it into the URL field and tap **💾 Save Custom URL**.
3. Tap **`📱 User (u100)`** or **`👑 Admin`** to sign in instantly.

---

## ⚡ Quick 1-Click Launchers

For your convenience, helper scripts are provided in `F:\Projects\DeptChatSystem\`:

- **`start_tunnel.bat`**: Double-click to start the Cloudflare tunnel with the optimized `--protocol http2` flag.
- **`start_backend_and_tunnel.bat`**: Double-click to start both the WebApi backend and the Cloudflare tunnel in separate windows.

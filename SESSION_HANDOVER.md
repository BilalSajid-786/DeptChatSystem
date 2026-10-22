# DeptChatSystem — Session Handover & Architecture Document

This document serves as the complete technical handover file for subsequent development sessions, containing the architecture blueprint, current system state, running services, recent implementations, and instructions for next steps.

---

## 1. Project Overview & Repository Structure
- **Repository Root**: `F:\Projects\DeptChatSystem`
- **Active Git Branch**: `feature/ui-enhancements-and-fixes`
- **Target Framework**: .NET 10 (`net10.0`, `net10.0-android`)

### Solution Projects (`DeptChatSystem.sln`):
1. **`DeptChat.Shared`** (`F:\Projects\DeptChatSystem\DeptChat.Shared`):
   - Shared cross-platform library.
   - Enums: `UserRole` (`Admin`, `Manager`, `User`), `MessageDeliveryStatus` (`Pending`, `Sent`, `Delivered`, `Read`).
   - DTOs: `ChatMessageDto`, `ConversationDto`, `DepartmentDto`, `AuthResponseDto`, `UserPresenceDto`, `InAppNotificationToastDto`, etc.
2. **`DeptChat.WebApi`** (`F:\Projects\DeptChatSystem\DeptChat.WebApi`):
   - ASP.NET Core Web API with SignalR real-time hubs.
   - Database: Entity Framework Core with SQL Server / Local SQLite.
   - Real-time Hub: `ChatHub` mapped at `/hubs/chat` (SignalR).
   - Controllers: `AuthController`, `ConversationsController`, `MessagesController`, `DepartmentsController`.
   - Push Notifications: `FirebaseNotificationService` via Firebase Admin SDK (`firebase-adminsdk.json`).
3. **`DeptChat.Mobile`** (`F:\Projects\DeptChatSystem\DeptChat.Mobile`):
   - .NET MAUI 10 mobile client targeting Android.
   - MVVM pattern via CommunityToolkit.Mvvm (`ObservableObject`, `[RelayCommand]`, `[ObservableProperty]`).
   - Offline Outbox & local cache powered by SQLite (`LocalOutboxService.cs`).
   - Audio recording & playback via native Android `MediaRecorder` & `MediaPlayer` (`AndroidAudioService.cs`).

---

## 2. Active Infrastructure & Network URLs
The backend is currently running locally and forwarded to the internet:

| Component | Endpoint / URL | Notes |
| :--- | :--- | :--- |
| **Cloudflare Public Tunnel** | `https://practitioners-ribbon-prevent-manufacturing.trycloudflare.com` | Active public HTTPS proxy via `cloudflared.exe`. Works globally on Wi-Fi and Cellular. |
| **Local Wi-Fi LAN** | `http://192.168.0.108:5051` | Direct IP for phones connected to the same local Wi-Fi router. |
| **Local Host** | `http://127.0.0.1:5051` | Local loopback for host machine. |
| **SignalR Hub** | `<ServerUrl>/hubs/chat` | WebSocket / Long-Polling SignalR connection. |

### How to Run Background Daemons:
- **Run Web API**:
  ```powershell
  cd F:\Projects\DeptChatSystem\DeptChat.WebApi
  .\bin\Debug\net10.0\DeptChat.WebApi.exe --urls "http://0.0.0.0:5051"
  ```
- **Run Cloudflare Tunnel**:
  ```powershell
  & "F:\Projects\DeptChatSystem\tools\cloudflared.exe" tunnel --url http://127.0.0.1:5051
  ```

---

## 3. Key Completed Implementations & Fixes

### A. Role-Based Chat Header Title (`ChatRoomViewModel.cs`)
- **Requirement**: Customers (`UserRole.User`, e.g. `u100`) must always see the **Department Name** (e.g. *"Complaints & Feedback"*). Staff/Managers (`UserRole.Manager`, `Admin`) must see the customer's name (`CustomerName`) they are assisting.
- **Fix**: In [`ChatRoomViewModel.cs`](file:///F:/Projects/DeptChatSystem/DeptChat.Mobile/ViewModels/ChatRoomViewModel.cs), `ContactTitle` and `DisplayTitle` inspect the user role via `AuthService`:
  ```csharp
  public string ContactTitle => (_authService?.CurrentUser?.Role == UserRole.User || _authService?.Role == UserRole.User)
      ? DepartmentName
      : (!string.IsNullOrWhiteSpace(CustomerName) ? CustomerName : DepartmentName);
  ```
- Background synchronization guards prevent `CustomerName` from being overwritten with `conv.CreatedByUserName` when the current user is a customer.

### B. WhatsApp-Style Audio Messaging & Scrubber Dragging
- **Live Recording Mode**: Red dot indicator, live timer (`0:05`), "⏸ Listen" preview button, and "🗑️" discard button.
- **Audio Preview Mode**: Allows user to preview recorded audio before sending with playable scrubber and duration tracker (`0:00 / 0:12`).
- **Scrubber Dragging Snapping Fix**:
  - `DragStarted="OnMessageSliderDragStarted"` and `DragCompleted="OnMessageSliderDragCompleted"` wired in [`ChatRoomPage.xaml`](file:///F:/Projects/DeptChatSystem/DeptChat.Mobile/Views/ChatRoomPage.xaml).
  - While dragging, `item.IsDragging = true` prevents the 100ms background timer from overwriting the slider value.
  - On release, `SeekMessageAudioAsync` synchronizes `_playbackElapsedSeconds`, seeks the native Android `MediaPlayer` via `_audioService.SeekToAsync(progress)`, and smoothly continues playback from the dropped timestamp without snapping back.
  - Pausing retains the current scrubber position (matching WhatsApp behavior).
  - Native Android `PlayAudioAsync` supports optional `startPositionMs` offset to resume from any timestamp.

### C. WhatsApp UI & Android Keyboard Avoidance
- Chat background features custom WhatsApp doodle wallpaper ([`Resources/Images/whatsapp_bg.svg`](file:///F:/Projects/DeptChatSystem/DeptChat.Mobile/Resources/Images/whatsapp_bg.svg)).
- Message bubbles styled with WhatsApp green (`#E7FFDB`) for outgoing messages, grey/blue delivery ticks (`✓✓`), and WhatsApp typography.
- Keyboard overlap resolved in [`MainActivity.cs`](file:///F:/Projects/DeptChatSystem/DeptChat.Mobile/Platforms/Android/MainActivity.cs):
  ```csharp
  AndroidX.Core.View.WindowCompat.SetDecorFitsSystemWindows(Window, true);
  Window.SetSoftInputMode(Android.Views.SoftInput.AdjustResize);
  ```

### D. Offline Outbox & Server Deletion Reconciliation
- Messages are saved to local SQLite before network transmission for 0ms optimistic UI rendering.
- Incremental sequence numbers (`SequenceNumber`) ensure strictly ordered message display.
- Server-side conversation deletion or clearance returns HTTP 404 / 0 sequence on delta sync, automatically clearing stale cached messages from the mobile SQLite outbox.

### E. Admin/Manager Customer Search Bar (`DepartmentConversationsPage.xaml`)
- **Requirement**: Admins and Managers opening a department with many customer inquiries can quickly find specific users or messages.
- **Implementation**:
  - Added a search bar in [`DepartmentConversationsPage.xaml`](file:///F:/Projects/DeptChatSystem/DeptChat.Mobile/Views/DepartmentConversationsPage.xaml) with instant reactive filtering.
  - In [`DepartmentConversationsViewModel.cs`](file:///F:/Projects/DeptChatSystem/DeptChat.Mobile/ViewModels/DepartmentConversationsViewModel.cs), `ApplyFilter()` matches against `CreatedByUserName` and `LastMessagePreview`.
  - Subtitle dynamically reflects match counts (e.g. `"1 of 25 Found"`).
  - Clear "✕" button resets search query immediately.
  - Dedicated "No Users Found" empty state with "Clear Search" button when query matches 0 items.

---

## 4. Key Code Locations & Symbols

| File Path | Role & Responsibilities |
| :--- | :--- |
| [`DeptChat.Mobile/ViewModels/ChatRoomViewModel.cs`](file:///F:/Projects/DeptChatSystem/DeptChat.Mobile/ViewModels/ChatRoomViewModel.cs) | Core chat logic, audio recording/preview/playback, SignalR event subscriptions, delivery status ticks, outbox queue. |
| [`DeptChat.Mobile/Views/ChatRoomPage.xaml`](file:///F:/Projects/DeptChatSystem/DeptChat.Mobile/Views/ChatRoomPage.xaml) | Chat UI, wallpaper background, speech bubbles, audio sliders, bottom WhatsApp floating pill input. |
| [`DeptChat.Mobile/Platforms/Android/AndroidAudioService.cs`](file:///F:/Projects/DeptChatSystem/DeptChat.Mobile/Platforms/Android/AndroidAudioService.cs) | Android `MediaRecorder` & `MediaPlayer` implementation of `IAudioService`. Handles AAC recording and seeking. |
| [`DeptChat.Mobile/Services/ServerConfigService.cs`](file:///F:/Projects/DeptChatSystem/DeptChat.Mobile/Services/ServerConfigService.cs) | Centralized server URL resolver. Automatically falls back to Cloudflare or Wi-Fi. |
| [`DeptChat.WebApi/Hubs/ChatHub.cs`](file:///F:/Projects/DeptChatSystem/DeptChat.WebApi/Hubs/ChatHub.cs) | SignalR hub handling message broadcast, typing indicators, presence, and read receipts (`MarkAsRead`). |
| [`DeptChat.WebApi/Controllers/ConversationsController.cs`](file:///F:/Projects/DeptChatSystem/DeptChat.WebApi/Controllers/ConversationsController.cs) | Conversation lifecycle, delta synchronization (`/sync`), message deletion reconciliation. |

---

## 5. Test Credentials & Accounts

| Email / Username | Password | Role | Notes |
| :--- | :--- | :--- | :--- |
| `u100@example.com` / `u100` | `Password123!` | Customer (`UserRole.User`) | Standard customer account. Interacts with departments. |
| `admin@example.com` | `Password123!` | Admin (`UserRole.Admin`) | Full administrative access to all department chats. |
| `manager@example.com` | `Password123!` | Manager (`UserRole.Manager`) | Department manager view. Assists assigned customers. |

---

## 6. How to Build & Distribute APK
To publish a signed release APK without requiring a manual keystore:
```powershell
dotnet publish F:\Projects\DeptChatSystem\DeptChat.Mobile\DeptChat.Mobile.csproj -f net10.0-android -c Release -p:AndroidPackageFormat=apk -p:AndroidKeyStore=false
```
- **Output APK**: `DeptChat.Mobile\bin\Release\net10.0-android\publish\com.companyname.deptchat.mobile-Signed.apk`
- **Root Convenience Copy**: `F:\Projects\DeptChatSystem\DeptChat.apk`

---

## 7. Recommended Next Implementation Steps
1. **Real-time Online & Last Seen Backend Logic**:
   - WebApi currently tracks in-memory presence; hook up full heartbeat tracking via SignalR `OnConnectedAsync` / `OnDisconnectedAsync` and persist `LastSeenAt` in the `Users` table.
2. **Audio Waveform Visualization**:
   - Currently, audio uses a sleek MAUI `<Slider>`. A custom waveform bar component (using SkiaSharp or MAUI Graphics) can be added for audio message visualizations.
3. **Persistent Cloudflare Tunnel (Named Tunnel)**:
   - To avoid changing the `trycloudflare.com` domain across system reboots, configure a named Cloudflare Tunnel (`cloudflared tunnel create deptchat`) with a persistent DNS CNAME or custom domain.

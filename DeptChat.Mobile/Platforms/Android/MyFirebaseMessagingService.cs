using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Firebase.Messaging;

namespace DeptChat.Mobile.Platforms.Android;

[Service(Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public class MyFirebaseMessagingService : FirebaseMessagingService
{
    public const string ChannelId = "deptchat_messages";

    public override void OnNewToken(string token)
    {
        base.OnNewToken(token);
        Preferences.Default.Set("fcm_device_token", token);
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);

        // Check if user is currently looking at this active conversation
        if (message.Data != null && message.Data.TryGetValue("conversationId", out var convIdStr) && Guid.TryParse(convIdStr, out var convId))
        {
            if (ViewModels.ChatRoomViewModel.ActiveConversationId == convId)
            {
                // User is actively in the chat room; suppress duplicate notification banner
                return;
            }
        }

        string title = "New Message";
        string body = "You have a new department chat message.";

        if (message.GetNotification() != null)
        {
            title = message.GetNotification().Title ?? title;
            body = message.GetNotification().Body ?? body;
        }
        else if (message.Data != null && message.Data.Count > 0)
        {
            if (message.Data.ContainsKey("title"))
                title = message.Data["title"];
            if (message.Data.ContainsKey("body"))
                body = message.Data["body"];
        }

        string? tappedConversationId = null;
        if (message.Data != null && message.Data.TryGetValue("conversationId", out var dataConversationId))
        {
            tappedConversationId = dataConversationId;
        }

        // When the message was sent (from the server), so a late push still shows the right time.
        long? sentAtMillis = null;
        if (message.Data != null && message.Data.TryGetValue("sentAt", out var sentAtText) &&
            DateTimeOffset.TryParse(sentAtText, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var sentAt))
        {
            sentAtMillis = sentAt.ToUnixTimeMilliseconds();
        }
        SendNotification(title, SanitizeNotificationBody(body), tappedConversationId, sentAtMillis);
    }

    private static string SanitizeNotificationBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return body;

        var colonIdx = body.IndexOf(':');
        string prefix = string.Empty;
        string content = body;
        if (colonIdx > 0 && colonIdx < body.Length - 1)
        {
            prefix = body.Substring(0, colonIdx + 1) + " ";
            content = body.Substring(colonIdx + 1).Trim();
        }

        if (content.StartsWith("[voice:", StringComparison.OrdinalIgnoreCase))
            return $"{prefix}🎙️ Voice message";
        if (content.StartsWith("[image:", StringComparison.OrdinalIgnoreCase))
            return $"{prefix}📷 Photo";
        if (content.StartsWith("[file:", StringComparison.OrdinalIgnoreCase))
        {
            var inner = content.Trim('[', ']');
            var parts = inner.Split(':');
            var docName = (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2])) ? $"📄 {parts[2]}" : "📎 Document";
            return $"{prefix}{docName}";
        }

        return body;
    }

    private void SendNotification(string title, string body, string? conversationId = null, long? sentAtMillis = null)
    {
        var notificationId = Random.Shared.Next(1000, 9999);

        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        // Tapping opens this chat (handled in MainActivity, same key as Android's own notifications).
        if (!string.IsNullOrEmpty(conversationId)) intent.PutExtra("conversationId", conversationId);
        // Own request code per notification, so each one opens its own chat.
        var pendingIntent = PendingIntent.GetActivity(this, notificationId, intent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var notificationManager = (NotificationManager?)GetSystemService(NotificationService);
        if (notificationManager == null) return;

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var channel = new NotificationChannel(ChannelId, "Department Chat Notifications", NotificationImportance.High)
            {
                Description = "Incoming messages and alerts from department chat",
                LockscreenVisibility = NotificationVisibility.Public
            };
            channel.EnableVibration(true);
            notificationManager.CreateNotificationChannel(channel);
        }

        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_stat_chat)
            .SetColor(global::Android.Graphics.Color.ParseColor("#008069"))
            .SetContentTitle(title)
            .SetContentText(body)
            .SetAutoCancel(true)
            .SetPriority((int)NotificationPriority.High)
            .SetDefaults((int)NotificationDefaults.All)
            .SetContentIntent(pendingIntent);

        if (sentAtMillis.HasValue)
        {
            builder.SetWhen(sentAtMillis.Value).SetShowWhen(true);
        }

        notificationManager.Notify(notificationId, builder.Build());
    }
}

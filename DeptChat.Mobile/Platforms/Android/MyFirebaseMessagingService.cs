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

        SendNotification(title, body);
    }

    private void SendNotification(string title, string body)
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        var pendingIntent = PendingIntent.GetActivity(this, 0, intent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

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
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentTitle(title)
            .SetContentText(body)
            .SetAutoCancel(true)
            .SetPriority((int)NotificationPriority.High)
            .SetDefaults((int)NotificationDefaults.All)
            .SetContentIntent(pendingIntent);

        notificationManager.Notify(Random.Shared.Next(1000, 9999), builder.Build());
    }
}

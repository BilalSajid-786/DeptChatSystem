using DeptChat.Application.Common.Interfaces;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DeptChat.Infrastructure.Services;

public class FirebasePushNotificationService : IPushNotificationService
{
    private readonly ILogger<FirebasePushNotificationService> _logger;
    private readonly bool _isConfigured;

    public FirebasePushNotificationService(IConfiguration configuration, ILogger<FirebasePushNotificationService> logger)
    {
        _logger = logger;

        try
        {
            if (FirebaseApp.DefaultInstance == null)
            {
                var credentialPath = configuration["Firebase:CredentialPath"] ?? "firebase-admin-key.json";
                if (File.Exists(credentialPath))
                {
                    FirebaseApp.Create(new AppOptions
                    {
                        Credential = GoogleCredential.FromFile(credentialPath)
                    });
                    _isConfigured = true;
                    _logger.LogInformation("✅ [FIREBASE ADMIN SDK] Successfully initialized with credentials from '{Path}'.", credentialPath);
                }
                else
                {
                    _logger.LogInformation("ℹ️ [FIREBASE ADMIN SDK] Credential file '{Path}' not found. FCM push notifications will run in simulation mode.", credentialPath);
                }
            }
            else
            {
                _isConfigured = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [FIREBASE ADMIN SDK] Error during initialization.");
        }
    }

    public async Task SendPushNotificationAsync(IEnumerable<string> deviceTokens, string title, string body, Dictionary<string, string>? data = null)
    {
        var tokenList = deviceTokens.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
        if (tokenList.Count == 0)
        {
            return;
        }

        _logger.LogInformation("🚀 [FCM PUSH DISPATCH] Triggering High-Priority Push to {Count} device(s). Title: '{Title}', Body: '{Body}'",
            tokenList.Count, title, body);

        if (_isConfigured && FirebaseMessaging.DefaultInstance != null)
        {
            try
            {
                var message = new MulticastMessage
                {
                    Tokens = tokenList,
                    Notification = new Notification
                    {
                        Title = title,
                        Body = body
                    },
                    Data = data ?? new Dictionary<string, string>(),
                    Android = new AndroidConfig
                    {
                        Priority = Priority.High,
                        Notification = new AndroidNotification
                        {
                            Sound = "default",
                            ChannelId = "deptchat_messages"
                        }
                    }
                };

                var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
                _logger.LogInformation("✅ [FCM PUSH DISPATCH SUCCESS] Delivered: {SuccessCount}, Failed: {FailureCount}",
                    response.SuccessCount, response.FailureCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [FCM PUSH DISPATCH FAILED] Error sending notification through Firebase Admin SDK.");
            }
        }

        await Task.CompletedTask;
    }
}

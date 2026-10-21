namespace DeptChat.Application.Common.Interfaces;

public interface IPushNotificationService
{
    Task SendPushNotificationAsync(IEnumerable<string> deviceTokens, string title, string body, Dictionary<string, string>? data = null);
}

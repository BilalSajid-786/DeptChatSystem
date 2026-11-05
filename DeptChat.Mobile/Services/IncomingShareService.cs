namespace DeptChat.Mobile.Services;

public class IncomingShareService
{
    private static IncomingShareService? _instance;
    public static IncomingShareService Instance => _instance ??= new IncomingShareService();

    public string? PendingSharedFilePath { get; set; }
    public string? PendingSharedFileName { get; set; }
    public string? PendingSharedMimeType { get; set; }

    public bool HasPendingShare => !string.IsNullOrEmpty(PendingSharedFilePath) && File.Exists(PendingSharedFilePath);

    public event Action<string>? OnPendingShareReceived;

    public void SetPendingShare(string filePath, string? fileName = null, string? mimeType = null)
    {
        PendingSharedFilePath = filePath;
        PendingSharedFileName = fileName ?? Path.GetFileName(filePath);
        PendingSharedMimeType = mimeType;
        OnPendingShareReceived?.Invoke(filePath);
    }

    public void ClearPendingShare()
    {
        PendingSharedFilePath = null;
        PendingSharedFileName = null;
        PendingSharedMimeType = null;
    }
}

namespace DeptChat.Mobile.Services;

public interface IAudioService
{
    bool IsRecording { get; }
    bool IsPlaying { get; }
    Task<bool> StartRecordingAsync(string outputPath);
    Task<string?> StopRecordingAsync();
    Task<bool> PlayAudioAsync(string filePath, Action? onFinished = null);
    Task StopAudioAsync();
}

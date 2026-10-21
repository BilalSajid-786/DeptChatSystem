namespace DeptChat.Mobile.Services;

public interface IAudioService
{
    bool IsRecording { get; }
    bool IsPlaying { get; }
    Task<bool> StartRecordingAsync(string outputPath);
    Task<string?> StopRecordingAsync();
    Task<bool> PlayAudioAsync(string filePath, Action? onFinished = null);
    Task StopAudioAsync();
    Task SeekToAsync(double progress);
    int GetDurationMs();
    int GetCurrentPositionMs();
}

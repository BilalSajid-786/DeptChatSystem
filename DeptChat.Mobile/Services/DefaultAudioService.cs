namespace DeptChat.Mobile.Services;

public class DefaultAudioService : IAudioService
{
    public bool IsRecording => false;
    public bool IsPlaying => false;

    public Task<bool> StartRecordingAsync(string outputPath) => Task.FromResult(false);
    public Task<string?> StopRecordingAsync() => Task.FromResult<string?>(null);
    public Task<bool> PlayAudioAsync(string filePath, Action? onFinished = null) => Task.FromResult(false);
    public Task StopAudioAsync() => Task.CompletedTask;
    public Task SeekToAsync(double progress) => Task.CompletedTask;
    public int GetDurationMs() => 0;
    public int GetCurrentPositionMs() => 0;
}

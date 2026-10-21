namespace DeptChat.Mobile.Services;

public class DefaultAudioService : IAudioService
{
    public bool IsRecording => false;
    public bool IsPlaying => false;

    public Task<bool> StartRecordingAsync(string outputPath) => Task.FromResult(false);
    public Task<string?> StopRecordingAsync() => Task.FromResult<string?>(null);
    public Task<bool> PlayAudioAsync(string filePath, Action? onFinished = null) => Task.FromResult(false);
    public Task StopAudioAsync() => Task.CompletedTask;
}

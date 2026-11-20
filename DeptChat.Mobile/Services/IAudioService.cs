namespace DeptChat.Mobile.Services;

public interface IAudioService
{
    bool IsRecording { get; }
    bool IsPlaying { get; }
    Task<bool> StartRecordingAsync(string outputPath);
    Task<string?> StopRecordingAsync();
    Task<bool> PlayAudioAsync(string filePath, Action? onFinished = null, int startPositionMs = 0, float speed = 1f);
    /// <summary>Changes the speed of the voice message that is playing now (1x, 1.5x, 2x).</summary>
    void SetPlaybackSpeed(float speed);
    /// <summary>Loads the short "message sent" sound so the first play is instant. Safe to call repeatedly.</summary>
    void PreloadSounds();
    /// <summary>Plays the short "message sent" sound (skipped when the phone is on silent or vibrate).</summary>
    void PlayMessageSentSound();
    Task StopAudioAsync();
    Task SeekToAsync(double progress);
    int GetDurationMs();
    int GetCurrentPositionMs();
}

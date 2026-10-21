using Android.Content;
using Android.Media;
using Android.OS;
using DeptChat.Mobile.Services;

namespace DeptChat.Mobile.Platforms.Android;

public class AndroidAudioService : IAudioService
{
    private MediaRecorder? _recorder;
    private MediaPlayer? _player;
    private string? _currentRecordingPath;
    private Action? _playbackFinishedCallback;

    public bool IsRecording => _recorder != null;
    public bool IsPlaying => _player != null && _player.IsPlaying;

    public Task<bool> StartRecordingAsync(string outputPath)
    {
        try
        {
            _ = StopAudioAsync();
            if (_recorder != null)
            {
                try { _recorder.Stop(); } catch { }
                _recorder.Release();
                _recorder = null;
            }

            _currentRecordingPath = outputPath;

            var context = global::Android.App.Application.Context;
            _recorder = Build.VERSION.SdkInt >= BuildVersionCodes.S
                ? new MediaRecorder(context)
                : new MediaRecorder();

            _recorder.SetAudioSource(AudioSource.Mic);
            _recorder.SetOutputFormat(OutputFormat.Mpeg4);
            _recorder.SetAudioEncoder(AudioEncoder.Aac);
            _recorder.SetAudioEncodingBitRate(32000);
            _recorder.SetAudioSamplingRate(22050);
            _recorder.SetOutputFile(outputPath);
            _recorder.Prepare();
            _recorder.Start();
            return Task.FromResult(true);
        }
        catch
        {
            _recorder?.Release();
            _recorder = null;
            return Task.FromResult(false);
        }
    }

    public Task<string?> StopRecordingAsync()
    {
        try
        {
            if (_recorder != null)
            {
                _recorder.Stop();
                _recorder.Release();
                _recorder = null;
                return Task.FromResult<string?>(_currentRecordingPath);
            }
        }
        catch
        {
            _recorder?.Release();
            _recorder = null;
        }

        return Task.FromResult<string?>(null);
    }

    public Task<bool> PlayAudioAsync(string filePath, Action? onFinished = null)
    {
        try
        {
            _ = StopAudioAsync();

            _playbackFinishedCallback = onFinished;
            _player = new MediaPlayer();
            _player.SetDataSource(filePath);
            _player.Completion += (s, e) =>
            {
                _ = StopAudioAsync();
                _playbackFinishedCallback?.Invoke();
            };

            _player.Prepare();
            _player.Start();
            return Task.FromResult(true);
        }
        catch
        {
            _ = StopAudioAsync();
            return Task.FromResult(false);
        }
    }

    public Task StopAudioAsync()
    {
        try
        {
            if (_player != null)
            {
                if (_player.IsPlaying)
                {
                    _player.Stop();
                }
                _player.Release();
                _player = null;
            }
        }
        catch { }

        return Task.CompletedTask;
    }
}

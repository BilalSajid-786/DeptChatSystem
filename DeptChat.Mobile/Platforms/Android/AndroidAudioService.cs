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
    public bool IsPlaying
    {
        get
        {
            // Read from a timer thread too, so the player may be released at the same moment.
            try { var p = _player; return p != null && p.IsPlaying; }
            catch { return false; }
        }
    }

    private float _speed = 1f;

    private SoundPool? _soundPool;
    private int _sentSoundId;
    private volatile bool _sentSoundLoaded;
    private readonly object _soundGate = new();

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

    public Task<bool> PlayAudioAsync(string filePath, Action? onFinished = null, int startPositionMs = 0, float speed = 1f)
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
            if (startPositionMs > 0 && startPositionMs < _player.Duration)
            {
                _player.SeekTo(startPositionMs);
            }
            _speed = speed;
            if (Math.Abs(speed - 1f) > 0.01f)
            {
                try { ApplySpeed(_player); } catch { }
            }
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

    public Task SeekToAsync(double progress)
    {
        try
        {
            if (_player != null && _player.Duration > 0)
            {
                var targetMs = (int)(Math.Clamp(progress, 0.0, 1.0) * _player.Duration);
                _player.SeekTo(targetMs);
            }
        }
        catch { }

        return Task.CompletedTask;
    }

    public int GetDurationMs()
    {
        try { return _player?.Duration ?? 0; } catch { return 0; }
    }

    public int GetCurrentPositionMs()
    {
        try { return _player?.CurrentPosition ?? 0; } catch { return 0; }
    }

    public void SetPlaybackSpeed(float speed)
    {
        _speed = speed;
        try
        {
            var p = _player;
            if (p != null && p.IsPlaying) ApplySpeed(p);
        }
        catch { }
    }

    private void ApplySpeed(MediaPlayer player)
    {
        var current = player.PlaybackParams ?? new PlaybackParams();
        player.PlaybackParams = current.SetSpeed(_speed) ?? current;
    }

    public void PreloadSounds()
    {
        lock (_soundGate)
        {
            if (_soundPool != null) return;
            try
            {
                var attributes = new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.AssistanceSonification)!
                    .SetContentType(AudioContentType.Sonification)!
                    .Build();
                var pool = new SoundPool.Builder().SetMaxStreams(1)!.SetAudioAttributes(attributes)!.Build()!;
                pool.LoadComplete += (s, e) =>
                {
                    if (e.Status == 0 && e.SampleId == _sentSoundId) _sentSoundLoaded = true;
                };
                _sentSoundId = pool.Load(global::Android.App.Application.Context, DeptChat.Mobile.Resource.Raw.message_sent, 1);
                _soundPool = pool;
            }
            catch
            {
                _soundPool = null;
            }
        }
    }

    public void PlayMessageSentSound()
    {
        try
        {
            var pool = _soundPool;
            if (pool == null || !_sentSoundLoaded)
            {
                PreloadSounds();
                return;
            }

            // Like WhatsApp: no sound when the phone is on silent or vibrate.
            var audioManager = global::Android.App.Application.Context.GetSystemService(Context.AudioService) as AudioManager;
            if (audioManager != null && audioManager.RingerMode != RingerMode.Normal) return;

            pool.Play(_sentSoundId, 0.6f, 0.6f, 1, 0, 1f);
        }
        catch { }
    }
}

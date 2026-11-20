using System.Collections.Concurrent;

namespace DeptChat.Mobile.Services;

/// <summary>
/// Real waveform bars for voice messages (WhatsApp style), read from the audio itself.
/// All work runs off the screen thread and is called one message at a time, only while the chat
/// list is still (see ChatRoomViewModel.ScheduleWaveforms). Results are tiny (one byte per bar),
/// kept in memory and saved in the app's cache folder, so each voice message is analysed once.
/// Cleared on logout. If anything fails, the row keeps its placeholder bars.
/// </summary>
public static class VoiceWaveforms
{
    public const int BarCount = 28;

    private static readonly ConcurrentDictionary<string, byte[]> Known = new();

    private static string CacheDir => Path.Combine(FileSystem.CacheDirectory, "voice_waveforms");

    /// <summary>Bars already worked out in this app run, or null. Cheap: memory only.</summary>
    public static byte[]? TryGetCached(string key) =>
        Known.TryGetValue(key, out var levels) ? levels : null;

    /// <summary>
    /// Reads (or loads saved) bar levels for one voice message. Slow: call from a background thread.
    /// If the audio file has to be created from the message data first, it is saved at
    /// <paramref name="audioCachePath"/> (the same file playback uses) and returned as AudioPath.
    /// </summary>
    public static (byte[]? Levels, string? AudioPath) Compute(string key, string? localAudioPath, string content, string audioCachePath)
    {
        try
        {
            var file = Path.Combine(CacheDir, SafeName(key) + ".wf");
            if (File.Exists(file))
            {
                var saved = File.ReadAllBytes(file);
                if (saved.Length == BarCount)
                {
                    Known[key] = saved;
                    return (saved, null);
                }
            }

            string? audioPath = !string.IsNullOrEmpty(localAudioPath) && File.Exists(localAudioPath) ? localAudioPath : null;
            string? createdAudioPath = null;
            if (audioPath == null)
            {
                audioPath = File.Exists(audioCachePath) ? audioCachePath : WriteVoiceAudioFile(content, audioCachePath);
                createdAudioPath = audioPath;
            }
            if (audioPath == null) return (null, null);

            var levels = ReadLevels(audioPath);
            if (levels == null) return (null, createdAudioPath);

            Directory.CreateDirectory(CacheDir);
            File.WriteAllBytes(file, levels);
            Known[key] = levels;
            return (levels, createdAudioPath);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Writes the audio inside a "[voice:data:audio/m4a;base64,...:seconds]" message to
    /// <paramref name="targetPath"/>. Written to a temporary file first and then renamed, so
    /// playback and the waveform reader never see a half-written file. Returns null on failure.
    /// </summary>
    public static string? WriteVoiceAudioFile(string content, string targetPath)
    {
        const string prefix = "[voice:data:audio/m4a;base64,";
        try
        {
            if (!content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            var lastColon = content.LastIndexOf(':');
            if (lastColon <= prefix.Length) return null;

            var bytes = Convert.FromBase64String(content.Substring(prefix.Length, lastColon - prefix.Length));
            var temp = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, targetPath, overwrite: true);
            return targetPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Called on logout: forgets and deletes all saved bars, and deletes the voice audio files kept
    /// in the cache folder (played / analysed voice messages "{id}.m4a", own recordings
    /// "voice_*.m4a" and any half-written ".tmp" copies), so the next user of the phone does not
    /// inherit the previous user's recordings.
    /// </summary>
    public static void Clear()
    {
        Known.Clear();
        var cacheRoot = FileSystem.CacheDirectory;
        _ = Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(CacheDir)) Directory.Delete(CacheDir, recursive: true);
            }
            catch { }

            DeleteFiles(cacheRoot, "*.m4a");
            DeleteFiles(cacheRoot, "*.m4a.*.tmp");
        });
    }

    private static void DeleteFiles(string folder, string pattern)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    }

    private static string SafeName(string key)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            key = key.Replace(c, '_');
        }
        return key;
    }

#if ANDROID
    /// <summary>
    /// Decodes the audio with Android's own decoder and keeps the loudest sample per bar.
    /// Gives up after a few seconds (very long or broken files keep the placeholder bars).
    /// </summary>
    private static byte[]? ReadLevels(string path)
    {
        using var extractor = new Android.Media.MediaExtractor();
        extractor.SetDataSource(path);

        Android.Media.MediaFormat? format = null;
        for (var i = 0; i < extractor.TrackCount; i++)
        {
            var candidate = extractor.GetTrackFormat(i);
            var trackMime = candidate.GetString(Android.Media.MediaFormat.KeyMime);
            if (trackMime != null && trackMime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            {
                extractor.SelectTrack(i);
                format = candidate;
                break;
            }
        }
        if (format == null) return null;

        var mime = format.GetString(Android.Media.MediaFormat.KeyMime)!;
        var durationUs = format.ContainsKey(Android.Media.MediaFormat.KeyDuration)
            ? format.GetLong(Android.Media.MediaFormat.KeyDuration)
            : 0;
        if (durationUs <= 0) return null;

        var peaks = new float[BarCount];
        var scratch = new byte[16384];
        var codec = Android.Media.MediaCodec.CreateDecoderByType(mime);
        try
        {
            codec.Configure(format, null, null, Android.Media.MediaCodecConfigFlags.None);
            codec.Start();

            var info = new Android.Media.MediaCodec.BufferInfo();
            var inputDone = false;
            var outputDone = false;
            var deadline = Environment.TickCount64 + 4000;

            while (!outputDone && Environment.TickCount64 < deadline)
            {
                if (!inputDone)
                {
                    var inIndex = codec.DequeueInputBuffer(10_000);
                    if (inIndex >= 0)
                    {
                        var inBuffer = codec.GetInputBuffer(inIndex)!;
                        var size = extractor.ReadSampleData(inBuffer, 0);
                        if (size < 0)
                        {
                            codec.QueueInputBuffer(inIndex, 0, 0, 0, Android.Media.MediaCodecBufferFlags.EndOfStream);
                            inputDone = true;
                        }
                        else
                        {
                            codec.QueueInputBuffer(inIndex, 0, size, extractor.SampleTime, Android.Media.MediaCodecBufferFlags.None);
                            extractor.Advance();
                        }
                    }
                }

                var outIndex = codec.DequeueOutputBuffer(info, 10_000);
                if (outIndex < 0) continue;

                if (info.Size > 0)
                {
                    var outBuffer = codec.GetOutputBuffer(outIndex)!;
                    var size = info.Size;
                    if (scratch.Length < size) scratch = new byte[size];
                    outBuffer.Position(info.Offset);
                    outBuffer.Limit(info.Offset + size);
                    outBuffer.Get(scratch, 0, size);

                    var bar = (int)Math.Clamp(info.PresentationTimeUs * BarCount / durationUs, 0, BarCount - 1);
                    var peak = peaks[bar];
                    // 16-bit samples; checking every 8th one is plenty for a bar's height.
                    for (var i = 0; i + 1 < size; i += 16)
                    {
                        int sample = (short)(scratch[i] | (scratch[i + 1] << 8));
                        if (sample < 0) sample = -sample;
                        if (sample > peak) peak = sample;
                    }
                    peaks[bar] = peak;
                }

                codec.ReleaseOutputBuffer(outIndex, false);
                if ((info.Flags & Android.Media.MediaCodecBufferFlags.EndOfStream) != 0) outputDone = true;
            }

            if (!outputDone) return null;
        }
        finally
        {
            try { codec.Stop(); } catch { }
            codec.Release();
        }

        var max = peaks.Max();
        var levels = new byte[BarCount];
        if (max < 1f) return levels; // silent recording: all bars at minimum height

        for (var i = 0; i < BarCount; i++)
        {
            // Square root lifts quiet speech so the bars look like WhatsApp's rather than mostly flat.
            var value = MathF.Sqrt(peaks[i] / max);
            levels[i] = (byte)Math.Round(255 * Math.Clamp(value, 0f, 1f));
        }
        return levels;
    }
#else
    private static byte[]? ReadLevels(string path) => null;
#endif
}

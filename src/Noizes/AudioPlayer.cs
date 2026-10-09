using NAudio.Wave;

namespace Noizes;

/// <summary>What happened to a requested play: played, deduplicated (a same-event play
/// fired again inside the dedup window), or sound-missing (no file at the path).</summary>
public record PlayResult(bool Played, string Reason);

/// <summary>Plays a sound file (mp3 or wav) on a background thread.</summary>
public static class AudioPlayer
{
    static readonly object _lock = new();
    static string _lastEvent = "";
    static DateTime _lastPlay = DateTime.MinValue;

    public static PlayResult Play(string eventId, string path, int volumePercent)
    {
        lock (_lock)
        {
            // ignore duplicate triggers within 1.2s (pollers/hooks can double-fire)
            if (eventId == _lastEvent && (DateTime.UtcNow - _lastPlay).TotalMilliseconds < 1200)
                return new PlayResult(false, "deduplicated");
            _lastEvent = eventId;
            _lastPlay = DateTime.UtcNow;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new PlayResult(false, "sound-missing");

        var t = new Thread(() => PlayFile(path, Math.Clamp(volumePercent, 0, 100) / 100f))
        {
            IsBackground = true
        };
        t.Start();
        return new PlayResult(true, "played");
    }

    static void PlayFile(string path, float volume)
    {
        try
        {
            using var reader = new AudioFileReader(path) { Volume = volume };
            using var output = new WaveOutEvent();
            output.Init(reader);
            output.Play();
            while (output.PlaybackState == PlaybackState.Playing)
                Thread.Sleep(100);
        }
        catch (Exception ex)
        {
            Logger.Info("audio play error: " + ex.Message);
        }
    }
}

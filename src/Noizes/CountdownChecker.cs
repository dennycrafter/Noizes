using System.Text.Json;
using System.Threading;
using Timer = System.Threading.Timer;

namespace Noizes;

/// <summary>
/// Tier 4: countdown - plays sounds at 1 hour, 30 minutes and 10 minutes before
/// a target date/time set in Settings. Each threshold fires once per target
/// (persisted in state.json so a restart does not replay them).
/// </summary>
public class CountdownChecker
{
    readonly HashSet<string> _fired = new();
    Timer _timer;

    public void Start()
    {
        LoadState();
        _timer = new Timer(_ => TickSafe(), null, TimeSpan.Zero, TimeSpan.FromSeconds(20));
        Logger.Info("countdown checker started");
    }

    public void Stop() => _timer?.Change(Timeout.Infinite, Timeout.Infinite);

    void LoadState()
    {
        try
        {
            if (!File.Exists(AppConfig.StatePath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(AppConfig.StatePath));
            if (doc.RootElement.TryGetProperty("countdownFired", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var v in arr.EnumerateArray())
                {
                    var s = v.GetString();
                    if (!string.IsNullOrEmpty(s)) _fired.Add(s);
                }
        }
        catch { }
    }

    void SaveState()
    {
        try
        {
            File.WriteAllText(AppConfig.StatePath, JsonSerializer.Serialize(new { countdownFired = _fired.ToList() }));
        }
        catch { }
    }

    void TickSafe()
    {
        try { Tick(); }
        catch (Exception ex) { Logger.Info("countdown error: " + ex.Message); }
    }

    void Tick()
    {
        var c = AppConfig.Current.Countdown;
        if (!c.Enabled) return;
        var remaining = c.TargetLocal - DateTime.Now;
        if (remaining <= TimeSpan.Zero) return;

        Check(c.TargetLocal, remaining, TimeSpan.FromMinutes(60), "countdown-1h");
        Check(c.TargetLocal, remaining, TimeSpan.FromMinutes(30), "countdown-30m");
        Check(c.TargetLocal, remaining, TimeSpan.FromMinutes(10), "countdown-10m");
    }

    void Check(DateTime target, TimeSpan remaining, TimeSpan threshold, string eventId)
    {
        if (remaining > threshold) return;
        var key = target.Ticks + ":" + threshold.TotalMinutes;
        if (!_fired.Add(key)) return;
        SaveState();
        Logger.Info($"countdown: {eventId}");
        EventBus.Dispatch(eventId);
    }
}

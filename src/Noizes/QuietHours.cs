namespace Noizes;

public static class QuietHours
{
    /// <summary>True if the event should be silenced right now. Alarms can be exempted.</summary>
    public static bool IsQuiet(AppConfig cfg, string eventId)
    {
        if (!cfg.Quiet.Enabled) return false;
        if (cfg.Quiet.AllowAlarms && eventId == "uptime-alarm") return false;
        if (!TimeSpan.TryParse(cfg.Quiet.Start, out var start)) return false;
        if (!TimeSpan.TryParse(cfg.Quiet.End, out var end)) return false;
        var now = DateTime.Now.TimeOfDay;
        if (start == end) return false;
        if (start < end) return now >= start && now < end;
        return now >= start || now < end; // overnight window (e.g. 22:00 -> 07:00)
    }
}

namespace Noizes;

public record DispatchResult(bool Played, string Reason);

/// <summary>Routes an event id through config, quiet hours and focus rules, then plays the sound.</summary>
public static class EventBus
{
    public static DispatchResult Dispatch(string eventId, string[] focusAppOverride = null)
    {
        var def = EventRegistry.Get(eventId);
        if (def == null)
        {
            Logger.Info($"event {eventId}: skipped (unknown-event)");
            return new DispatchResult(false, "unknown-event");
        }

        var cfg = AppConfig.Current;
        var ec = cfg.Events.TryGetValue(eventId, out var e) ? e : new EventConfig();

        if (!ec.Enabled)
        {
            Logger.Info($"event {eventId}: skipped (disabled)");
            return new DispatchResult(false, "disabled");
        }
        if (QuietHours.IsQuiet(cfg, eventId))
        {
            Logger.Info($"event {eventId}: skipped (quiet-hours)");
            return new DispatchResult(false, "quiet-hours");
        }

        if (cfg.OnlyWhenUnfocused)
        {
            var apps = focusAppOverride is { Length: > 0 }
                ? focusAppOverride
                : (ec.FocusApps.Count > 0 ? ec.FocusApps.ToArray() : def.DefaultFocusApps);
            if (FocusCheck.IsFocused(apps))
            {
                Logger.Info($"event {eventId}: skipped (source-focused)");
                return new DispatchResult(false, "source-focused");
            }
        }

        var path = ec.SoundPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            path = Path.Combine(AppConfig.SoundsDir, def.DefaultSound);

        bool played = AudioPlayer.Play(eventId, path, ec.Volume);
        Logger.Info($"event {eventId}: {(played ? "played" : "sound-missing")} ({path})");
        return new DispatchResult(played, played ? "played" : "sound-missing");
    }
}

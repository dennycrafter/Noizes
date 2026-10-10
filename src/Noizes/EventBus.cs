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

        // mute gate: first check after unknown-event, so a mute silences everything,
        // including the uptime alarm (which quiet hours would normally exempt)
        if (IsMuted(cfg))
        {
            Logger.Info($"event {eventId}: skipped (muted)");
            return new DispatchResult(false, "muted");
        }

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

        var play = AudioPlayer.Play(eventId, path, ec.Volume);
        Logger.Info($"event {eventId}: {play.Reason} ({path})");
        return new DispatchResult(play.Played, play.Reason);
    }

    /// <summary>
    /// True while Sounds off (Muted) or a timed mute (MutedUntil) is active.
    /// A MutedUntil in the past clears itself, so a stale "Mute 1 hour" never sticks.
    /// </summary>
    public static bool IsMuted(AppConfig cfg)
    {
        if (cfg.MutedUntil.HasValue && cfg.MutedUntil.Value <= DateTime.Now)
        {
            cfg.MutedUntil = null; // past: clear it automatically and persist that
            AppConfig.Save();
        }
        return IsMutedNow(cfg);
    }

    /// <summary>Side-effect-free mute check, for state reads (the bridge's state payload).</summary>
    public static bool IsMutedNow(AppConfig cfg) =>
        cfg.Muted || (cfg.MutedUntil.HasValue && cfg.MutedUntil.Value > DateTime.Now);
}

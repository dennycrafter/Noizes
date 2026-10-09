namespace Noizes;

public record EventDef(string Id, string DisplayName, string DefaultSound, string[] DefaultFocusApps, string Category);

/// <summary>Every event Noizes can play a sound for.</summary>
public static class EventRegistry
{
    public static readonly EventDef[] All = new[]
    {
        // Tier 1 - coding agents + GitHub core
        new EventDef("claude-code-done",    "Claude Code: finished",          "08-success.wav",  Array.Empty<string>(), "Coding & AI"),
        new EventDef("claude-code-input",   "Claude Code: needs your input",  "04-two-tone.wav", Array.Empty<string>(), "Coding & AI"),
        new EventDef("cursor-done",         "Cursor: agent finished",         "01-chime.wav",    Array.Empty<string>(), "Coding & AI"),
        new EventDef("gh-push",             "GitHub: new push",               "03-arp-up.wav",   Array.Empty<string>(), "GitHub"),
        new EventDef("gh-pr-opened",        "GitHub: PR opened",              "07-marimba.wav",  Array.Empty<string>(), "GitHub"),
        new EventDef("gh-pr-merged",        "GitHub: PR merged",              "08-success.wav",  Array.Empty<string>(), "GitHub"),
        new EventDef("gh-checks-failed",    "GitHub: checks failed",          "06-sparkle.wav",  Array.Empty<string>(), "GitHub"),
        // Tier 2 - Claude desktop app + browser
        new EventDef("claude-desktop-done", "Claude desktop app: finished",   "01-chime.wav",    new[]{"claude"},       "Apps"),
        new EventDef("browser-claude-done", "Browser: claude.ai finished",    "05-triad.wav",    new[]{"chrome"},       "Browser"),
        new EventDef("browser-obvious-done","Browser: Obvious finished",      "03-arp-up.wav",   new[]{"chrome"},       "Browser"),
        new EventDef("browser-watch-done",  "Browser: watched tab finished",  "02-bell.wav",     new[]{"chrome"},       "Browser"),
        new EventDef("browser-download-done","Browser: download finished",    "02-bell.wav",     new[]{"chrome"},       "Browser"),
        // Tier 3 - deploys + GitHub extras
        new EventDef("deploy-succeeded",    "Deploy: succeeded",              "08-success.wav",  Array.Empty<string>(), "Deploys"),
        new EventDef("deploy-failed",       "Deploy: failed",                 "06-sparkle.wav",  Array.Empty<string>(), "Deploys"),
        new EventDef("gh-star",             "GitHub: new star",               "07-marimba.wav",  Array.Empty<string>(), "GitHub"),
        new EventDef("gh-issue",            "GitHub: new issue",              "04-two-tone.wav", Array.Empty<string>(), "GitHub"),
        new EventDef("gh-comment",          "GitHub: new comment",            "05-triad.wav",    Array.Empty<string>(), "GitHub"),
        // Tier 4 - extras
        new EventDef("uptime-alarm",        "Uptime: a site is down",         "06-sparkle.wav",  Array.Empty<string>(), "Extras"),
        new EventDef("long-command-done",   "Long command finished",          "05-triad.wav",    Array.Empty<string>(), "Extras"),
        new EventDef("countdown-1h",        "Countdown: 1 hour left",         "04-two-tone.wav", Array.Empty<string>(), "Extras"),
        new EventDef("countdown-30m",       "Countdown: 30 minutes left",     "04-two-tone.wav", Array.Empty<string>(), "Extras"),
        new EventDef("countdown-10m",       "Countdown: 10 minutes left",     "03-arp-up.wav",   Array.Empty<string>(), "Extras"),
    };

    public static EventDef Get(string id) => All.FirstOrDefault(e => e.Id == id);
}

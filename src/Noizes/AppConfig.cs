using System.Text.Json;

namespace Noizes;

public class QuietHoursConfig
{
    public bool Enabled { get; set; } = false;
    public string Start { get; set; } = "22:00";
    public string End { get; set; } = "07:00";
    public bool AllowAlarms { get; set; } = true;
}

public class GitHubConfig
{
    public string Token { get; set; } = "";
    public string Username { get; set; } = "dennycrafter";
    public int PollSeconds { get; set; } = 10; // seconds between polls; clamped 5..300 at use
    // Repo feeds to watch in addition to the user feed, one "owner/repo" per entry.
    // The user feed only ever contains the user's own actions, so events by other
    // actors (GitHub Apps, collaborators) are only visible via /repos/{owner}/{repo}/events.
    public List<string> Repos { get; set; } = new();
}

public class CountdownConfig
{
    public bool Enabled { get; set; } = false;
    public DateTime TargetLocal { get; set; } = DateTime.Today.AddDays(1).AddHours(12);
}

public class FeaturesConfig
{
    public bool ClaudeDesktopWatcher { get; set; } = false; // UIA watcher is opt-in
}

/// <summary>Settings window size and position (v1.3.0). X or Y of -1 lets Windows place it.</summary>
public class WindowConfig
{
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public int Width { get; set; } = 1080;
    public int Height { get; set; } = 720;
    public bool Maximized { get; set; } = false;
}

public class EventConfig
{
    public bool Enabled { get; set; } = false;
    public int Volume { get; set; } = 40;
    public string SoundPath { get; set; } = "";
    public List<string> FocusApps { get; set; } = new();
}

public class AppConfig
{
    public int Port { get; set; } = 7351;
    public bool StartWithWindows { get; set; } = true;
    public bool OnlyWhenUnfocused { get; set; } = true;
    public QuietHoursConfig Quiet { get; set; } = new();
    public GitHubConfig GitHub { get; set; } = new();
    public List<string> UptimeUrls { get; set; } = new();
    public CountdownConfig Countdown { get; set; } = new();
    public FeaturesConfig Features { get; set; } = new();
    public Dictionary<string, EventConfig> Events { get; set; } = new();
    // v1.3.0: global mute (Sounds off), a timed mute (Mute 1 hour), and the window memory.
    // Added fields only - a v1.2.0 config.json loads unchanged and picks these up as defaults.
    public bool Muted { get; set; } = false;
    public DateTime? MutedUntil { get; set; } = null;
    public WindowConfig Window { get; set; } = new();

    public static string Dir => DirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Noizes");
    /// <summary>Test seam: SelfTest redirects the whole config/log dir so bridge saves never touch the real one.</summary>
    internal static string DirOverride;
    public static string FilePath => Path.Combine(Dir, "config.json");
    public static string StatePath => Path.Combine(Dir, "state.json");
    public static string SoundsDir => Path.Combine(AppContext.BaseDirectory, "sounds");

    public static AppConfig Current { get; private set; } = new();

    public static void EnsureDefaults(AppConfig cfg)
    {
        foreach (var def in EventRegistry.All)
            if (!cfg.Events.ContainsKey(def.Id))
                cfg.Events[def.Id] = new EventConfig();
        if (cfg.Window == null) cfg.Window = new WindowConfig(); // a hand-edited "Window": null must not crash
    }

    public static void Load()
    {
        var cfg = new AppConfig();
        try
        {
            if (File.Exists(FilePath))
            {
                var parsed = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath));
                if (parsed != null) cfg = parsed;
            }
        }
        catch (Exception ex)
        {
            Logger.Info("config load failed, using defaults: " + ex.Message);
        }
        EnsureDefaults(cfg);
        Current = cfg;
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Logger.Info("config save failed: " + ex.Message);
        }
    }
}

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
}

public class CountdownConfig
{
    public bool Enabled { get; set; } = false;
    public DateTime TargetLocal { get; set; } = DateTime.Today.AddDays(1).AddHours(12);
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
    public Dictionary<string, EventConfig> Events { get; set; } = new();

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Noizes");
    public static string FilePath => Path.Combine(Dir, "config.json");
    public static string StatePath => Path.Combine(Dir, "state.json");
    public static string SoundsDir => Path.Combine(AppContext.BaseDirectory, "sounds");

    public static AppConfig Current { get; private set; } = new();

    public static void EnsureDefaults(AppConfig cfg)
    {
        foreach (var def in EventRegistry.All)
            if (!cfg.Events.ContainsKey(def.Id))
                cfg.Events[def.Id] = new EventConfig();
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

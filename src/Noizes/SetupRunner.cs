using System.Runtime.InteropServices;
using System.Text.Json;

namespace Noizes;

/// <summary>
/// Headless one-command setup (Noizes.exe --setup): installs the Claude Code and Cursor
/// hooks on the real config files (idempotent, backs up first), enables exactly the four
/// core events at the default 40% volume, saves config, prints one line per step and
/// exits 0/1. Prompt-free and safe to run twice, so a human or an AI agent can run it.
/// </summary>
public static class SetupRunner
{
    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    /// <summary>The only events setup turns on - the locked v1.1.0 decision ("nothing else").</summary>
    public static readonly string[] CoreEvents =
    {
        "claude-code-done", "claude-code-input", "cursor-done", "long-command-done"
    };

    public class Options
    {
        /// <summary>Test override; null = the real ~/.claude/settings.json.</summary>
        public string ClaudePath;
        /// <summary>Test override; null = the real ~/.cursor/hooks.json.</summary>
        public string CursorPath;
        /// <summary>Test override; null = <see cref="CoreEvents"/>.</summary>
        public string[] EventsToEnable;
        /// <summary>Test override; null = the real %APPDATA%\Noizes\config.json.</summary>
        public string ConfigPath;
    }

    public static int Run(Options o)
    {
        try { AttachConsole(-1); } catch { } // no parent console (or running on Linux) - output simply dropped

        var problems = 0;

        // hooks embed the local server port, so config comes first
        var configPath = o.ConfigPath ?? AppConfig.FilePath;
        AppConfig cfg;
        var loaded = TryLoadConfig(configPath, out cfg);
        if (!loaded) problems++;

        var claude = ClaudeCodeConnector.Connect(o.ClaudePath, cfg.Port);
        Say("Claude Code: " + claude.message);
        if (!claude.ok) problems++;

        var cursor = CursorConnector.Connect(o.CursorPath, cfg.Port);
        Say("Cursor: " + cursor.message);
        if (!cursor.ok) problems++;

        foreach (var id in o.EventsToEnable ?? CoreEvents)
        {
            if (!cfg.Events.TryGetValue(id, out var ec))
            {
                Say($"event {id}: unknown event id - nothing enabled");
                problems++;
                continue;
            }
            ec.Enabled = true;
            ec.Volume = 40;
            Say($"event {id}: on at 40%");
        }

        // a config we could not read is never overwritten - its contents stay for manual repair
        if (loaded)
        {
            try
            {
                SaveConfig(cfg, configPath);
                Say($"config saved: {configPath}");
            }
            catch (Exception ex)
            {
                Say($"config save failed: {ex.Message}");
                problems++;
            }
        }
        else
        {
            Say($"config not saved: could not read {configPath} - fix or delete it, then run setup again");
        }

        Say(problems == 0
            ? "Setup complete. Restart Claude Code / Cursor so their new hooks load."
            : $"Setup finished with {problems} problem(s) - see the lines above.");
        return problems == 0 ? 0 : 1;
    }

    static void Say(string line)
    {
        try { Console.WriteLine("[noizes] " + line); } catch { } // no console attached
    }

    // mirrors AppConfig.Load/Save but at an injectable path, so the self-test (and any
    // future tooling) can run hermetically instead of touching the user's real config
    static bool TryLoadConfig(string path, out AppConfig cfg)
    {
        cfg = new AppConfig();
        try
        {
            if (File.Exists(path))
            {
                var parsed = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path));
                if (parsed != null) cfg = parsed;
            }
            AppConfig.EnsureDefaults(cfg);
            return true;
        }
        catch (Exception ex)
        {
            Say($"could not read {path} (not valid JSON: {ex.Message}) - continuing with defaults");
            cfg = new AppConfig();
            AppConfig.EnsureDefaults(cfg);
            return false;
        }
    }

    static void SaveConfig(AppConfig cfg, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
    }
}

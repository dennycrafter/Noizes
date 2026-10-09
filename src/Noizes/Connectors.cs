using System.Text.Json;
using System.Text.Json.Nodes;

namespace Noizes;

/// <summary>Adds Noizes hooks to a JSON config file: backs up first, never deletes, never duplicates.</summary>
public static class HookFileMerger
{
    public static (bool ok, string message) AddHook(string filePath, string key, JsonObject entry, string commandToCheck, bool ensureVersion = false)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            bool existed = File.Exists(filePath);
            JsonObject root;
            if (existed)
            {
                var text = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(text))
                {
                    root = new JsonObject();
                }
                else
                {
                    try
                    {
                        root = JsonNode.Parse(text, new JsonNodeOptions { PropertyNameCaseInsensitive = true }) as JsonObject;
                    }
                    catch (JsonException jex)
                    {
                        return (false, $"Could not read {filePath} (not valid JSON: {jex.Message}). Nothing was changed - you can connect manually instead.");
                    }
                    if (root == null)
                        return (false, $"Unexpected format in {filePath} (not a JSON object). Nothing was changed.");
                }
            }
            else
            {
                root = new JsonObject();
            }

            // idempotency: if this exact command is already present anywhere in the file, do nothing
            if (existed && File.ReadAllText(filePath).Contains(commandToCheck, StringComparison.Ordinal))
                return (true, "already");

            // back up the original before touching it
            if (existed)
            {
                var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Copy(filePath, filePath + ".noizes-backup-" + stamp, overwrite: false);
            }

            if (ensureVersion && !root.ContainsKey("version")) root["version"] = 1;
            if (root["hooks"] is not JsonObject hooks)
            {
                hooks = new JsonObject();
                root["hooks"] = hooks;
            }
            if (hooks[key] is not JsonArray arr)
            {
                arr = new JsonArray();
                hooks[key] = arr;
            }
            arr.Add(entry);

            File.WriteAllText(filePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return (true, existed ? "added" : "created");
        }
        catch (Exception ex)
        {
            return (false, "Failed: " + ex.Message);
        }
    }
}

public static class ClaudeCodeConnector
{
    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static (bool ok, string message) Connect(string settingsPathOverride, int port)
    {
        var path = settingsPathOverride ?? SettingsPath;
        var cmd1 = $"curl.exe -s -m 2 -X POST http://127.0.0.1:{port}/event/claude-code-done";
        var cmd2 = $"curl.exe -s -m 2 -X POST http://127.0.0.1:{port}/event/claude-code-input";

        var entry1 = new JsonObject
        {
            ["matcher"] = "",
            ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = cmd1 })
        };
        var r1 = HookFileMerger.AddHook(path, "Stop", entry1, cmd1);
        if (!r1.ok) return r1;

        var entry2 = new JsonObject
        {
            ["matcher"] = "",
            ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = cmd2 })
        };
        var r2 = HookFileMerger.AddHook(path, "Notification", entry2, cmd2);
        if (!r2.ok) return r2;

        if (r1.message == "already" && r2.message == "already")
            return (true, "Claude Code was already connected - existing hooks left untouched.");

        return (true, "Claude Code connected: the \"finished\" (Stop) and \"needs your input\" (Notification) hooks were added to " +
            path + " (the original file was backed up next to it). Open a NEW Claude Code session for the hooks to load.");
    }
}

public static class CursorConnector
{
    public static string HooksPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cursor", "hooks.json");

    public static (bool ok, string message) Connect(string hooksPathOverride, int port)
    {
        var path = hooksPathOverride ?? HooksPath;
        var cmd = $"curl.exe -s -m 2 -X POST http://127.0.0.1:{port}/event/cursor-done";
        var entry = new JsonObject { ["command"] = cmd };
        var r = HookFileMerger.AddHook(path, "stop", entry, cmd, ensureVersion: true);
        if (!r.ok) return r;
        if (r.message == "already")
            return (true, "Cursor was already connected - existing hooks left untouched.");
        return (true, "Cursor connected: the agent \"stop\" hook was added to " + path +
            ". Restart Cursor for it to load.");
    }
}

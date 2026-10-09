using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.Wave;

namespace Noizes;

/// <summary>
/// Headless verification mode: Noizes.exe --selftest
/// Runs on a real Windows machine (CI) without showing any UI or playing audio.
/// Writes PASS/FAIL lines to selftest.log and to the parent console.
/// </summary>
public static class SelfTest
{
    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    static readonly List<string> Lines = new();
    static int _fails;

    static void Check(string name, bool ok, string detail = "")
    {
        var line = $"{(ok ? "PASS" : "FAIL")} {name}{(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}";
        Lines.Add(line);
        if (!ok) _fails++;
    }

    public static int Run()
    {
        try { AttachConsole(-1); } catch { }

        // 1. config roundtrip
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "noizes-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var cfg = new AppConfig { Port = 9999 };
            cfg.Events["claude-code-done"].Volume = 42;
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, JsonSerializer.Serialize(cfg));
            var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path));
            Check("config-roundtrip",
                loaded != null && loaded.Port == 9999 && loaded.Events["claude-code-done"].Volume == 42);
        }
        catch (Exception ex) { Check("config-roundtrip", false, ex.Message); }

        // 2. Claude Code hook merge (with an existing hook that must survive)
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "noizes-selftest-cc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "settings.json");
            File.WriteAllText(file,
                "{\"model\":\"opus\",\"hooks\":{\"Stop\":[{\"matcher\":\"\",\"hooks\":[{\"type\":\"command\",\"command\":\"preexisting-hook\"}]}]}}");

            var r = ClaudeCodeConnector.Connect(file, 7351);
            var after = File.ReadAllText(file);
            var backups = Directory.GetFiles(dir, "*.noizes-backup-*").Length;
            Check("claude-code-merge", r.ok && after.Contains("claude-code-done") &&
                after.Contains("claude-code-input") && after.Contains("preexisting-hook") &&
                after.Contains("\"model\"") && backups >= 1,
                $"ok={r.ok} backups={backups}");

            var r2 = ClaudeCodeConnector.Connect(file, 7351);
            var after2 = File.ReadAllText(file);
            var count = after2.Split("claude-code-done").Length - 1;
            Check("claude-code-idempotent", r2.ok && count == 1, $"occurrences={count}");
        }
        catch (Exception ex) { Check("claude-code-merge", false, ex.Message); }

        // 3. Cursor hook merge (fresh file + corrupt file protection)
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "noizes-selftest-cu-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "hooks.json");
            var r = CursorConnector.Connect(file, 7351);
            var after = File.ReadAllText(file);
            var r2 = CursorConnector.Connect(file, 7351);
            var count = File.ReadAllText(file).Split("cursor-done").Length - 1;
            Check("cursor-merge", r.ok && after.Contains("\"version\"") && after.Contains("cursor-done"), $"ok={r.ok}");
            Check("cursor-idempotent", r2.ok && count == 1, $"occurrences={count}");

            var bad = Path.Combine(dir, "bad.json");
            File.WriteAllText(bad, "{not valid json");
            var r3 = CursorConnector.Connect(bad, 7351);
            Check("corrupt-config-untouched", !r3.ok && File.ReadAllText(bad) == "{not valid json");
        }
        catch (Exception ex) { Check("cursor-merge", false, ex.Message); }

        // 4. default sounds decode
        try
        {
            var sounds = Directory.GetFiles(AppConfig.SoundsDir, "*.wav");
            Check("sounds-present", sounds.Length >= 8, $"found {sounds.Length}");
            foreach (var s in sounds)
            {
                using var reader = new AudioFileReader(s);
                Check("sound-decode:" + Path.GetFileName(s), reader.TotalTime.TotalSeconds > 0.2);
            }
        }
        catch (Exception ex) { Check("sounds-present", false, ex.Message); }

        // 5. local server roundtrip (dry-run: no audio)
        try
        {
            var server = new HttpServer { DryRun = true };
            var port = 10000 + Random.Shared.Next(20000);
            server.Start(port);

            using var http = new HttpClient();
            var health = http.GetAsync($"http://127.0.0.1:{port}/health").Result;
            Check("http-health", (int)health.StatusCode == 200);

            var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/event/claude-code-done");
            req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            var resp = http.SendAsync(req).Result;
            var body = resp.Content.ReadAsStringAsync().Result;
            Check("http-event", (int)resp.StatusCode == 200 && body.Contains("claude-code-done"), body);

            var cors = resp.Headers.Contains("Access-Control-Allow-Origin");
            Check("http-cors", cors);

            var missing = http.GetAsync($"http://127.0.0.1:{port}/event/not-a-real-event").Result;
            Check("http-unknown-event-404", (int)missing.StatusCode == 404);

            server.Stop();
        }
        catch (Exception ex) { Check("http-event", false, ex.Message); }

        // 6. quiet hours logic
        try
        {
            var cfg = new AppConfig();
            cfg.Quiet.Enabled = false;
            Check("quiet-disabled", !QuietHours.IsQuiet(cfg, "claude-code-done"));
            cfg.Quiet.Enabled = true;
            cfg.Quiet.Start = "00:00";
            cfg.Quiet.End = "23:59";
            Check("quiet-window", QuietHours.IsQuiet(cfg, "claude-code-done"));
            Check("quiet-allow-alarms", !QuietHours.IsQuiet(cfg, "uptime-alarm"));
            cfg.Quiet.AllowAlarms = false;
            Check("quiet-block-alarms", QuietHours.IsQuiet(cfg, "uptime-alarm"));
        }
        catch (Exception ex) { Check("quiet-window", false, ex.Message); }

        // report
        var report = string.Join(Environment.NewLine, Lines) + Environment.NewLine +
                     $"SUMMARY: {Lines.Count - _fails} passed, {_fails} failed" + Environment.NewLine;
        File.WriteAllText("selftest.log", report);
        try
        {
            Console.Write(report);
            Console.Out.Flush();
        }
        catch { }

        return _fails == 0 ? 0 : 1;
    }
}

using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
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

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    static string RawRequest(int port, string raw)
    {
        using var c = new TcpClient();
        c.Connect(IPAddress.Loopback, port);
        var stream = c.GetStream();
        stream.ReadTimeout = 3000;
        var bytes = Encoding.UTF8.GetBytes(raw);
        stream.Write(bytes, 0, bytes.Length);
        var buf = new byte[8192];
        var sb = new StringBuilder();
        int n;
        try { while ((n = stream.Read(buf, 0, buf.Length)) > 0) sb.Append(Encoding.UTF8.GetString(buf, 0, n)); }
        catch { }
        return sb.ToString();
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
            AppConfig.EnsureDefaults(cfg);
            cfg.Events["claude-code-done"].Volume = 42;
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, JsonSerializer.Serialize(cfg));
            var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path));
            Check("config-roundtrip",
                loaded != null && loaded.Port == 9999 && loaded.Events["claude-code-done"].Volume == 42);
        }
        catch (Exception ex) { Check("config-roundtrip", false, ex.Message); }

        // 1b. fresh-install defaults: every event ships disabled at 40% volume
        try
        {
            var cfg = new AppConfig();
            AppConfig.EnsureDefaults(cfg);
            var bad = new List<string>();
            foreach (var def in EventRegistry.All)
                if (!cfg.Events.TryGetValue(def.Id, out var ec) || ec.Enabled || ec.Volume != 40)
                    bad.Add(def.Id);
            Check("defaults-off",
                bad.Count == 0 && cfg.Events.Count == EventRegistry.All.Length,
                bad.Count == 0 ? $"events={cfg.Events.Count}" : "bad: " + string.Join(",", bad));
        }
        catch (Exception ex) { Check("defaults-off", false, ex.Message); }

        // 1c. every persisted setting's default, asserted by name (owner directive: the
        // whole config surface, not just headline values). Two failure modes: a recorded
        // default that changed, and a config property with no recorded default - new
        // settings must state their default here or the sweep fails.
        try
        {
            var cfg = new AppConfig();
            AppConfig.EnsureDefaults(cfg);
            var problems = new List<string>();

            // the Countdown default is "noon the next day" evaluated at construction -
            // assert the shape, not a date literal
            bool NoonTomorrow(object v) =>
                v is DateTime d && d.Date == DateTime.Today.AddDays(1) && d.Hour == 12 && d.Minute == 0 && d.Second == 0;

            var expected = new (string Path, Func<object, bool> Match, string Describe)[]
            {
                ("Port", v => v is 7351, "7351"),
                ("StartWithWindows", v => v is true, "true - the tray app is present by default"),
                ("OnlyWhenUnfocused", v => v is true, "true"),
                ("Quiet.Enabled", v => v is false, "false"),
                ("Quiet.Start", v => v is "22:00", "\"22:00\""),
                ("Quiet.End", v => v is "07:00", "\"07:00\""),
                ("Quiet.AllowAlarms", v => v is true, "true"),
                ("GitHub.Token", v => v is "", "empty string"),
                ("GitHub.Username", v => v is "dennycrafter", "\"dennycrafter\""),
                ("GitHub.PollSeconds", v => v is 10, "10 (clamped 5..300 at use)"),
                ("GitHub.Repos", v => v is List<string> r && r.Count == 0, "empty"),
                ("UptimeUrls", v => v is List<string> u && u.Count == 0, "empty"),
                ("Countdown.Enabled", v => v is false, "false"),
                ("Countdown.TargetLocal", NoonTomorrow, "noon the day after creation"),
                ("Features.ClaudeDesktopWatcher", v => v is false, "false - UIA watcher is opt-in"),
                ("Muted", v => v is false, "false - sounds are on by default"),
                ("MutedUntil", v => v is null, "null - no timed mute by default"),
                ("Window.X", v => v is -1, "-1 - Windows places the window on first run"),
                ("Window.Y", v => v is -1, "-1 - Windows places the window on first run"),
                ("Window.Width", v => v is 1080, "1080 - the new-window default"),
                ("Window.Height", v => v is 720, "720 - the new-window default"),
                ("Window.Maximized", v => v is false, "false"),
            };

            object ReadPath(string path)
            {
                object current = cfg;
                foreach (var seg in path.Split('.'))
                {
                    var prop = current.GetType().GetProperty(seg);
                    if (prop == null)
                        throw new InvalidOperationException($"no property '{seg}' on {current.GetType().Name} (path {path})");
                    current = prop.GetValue(current);
                }
                return current;
            }

            foreach (var row in expected)
            {
                object actual;
                try { actual = ReadPath(row.Path); }
                catch (Exception ex) { problems.Add($"{row.Path}: {ex.Message}"); continue; }
                if (!row.Match(actual))
                    problems.Add($"{row.Path}: expected {row.Describe}, actual {(actual == null ? "null" : actual.ToString())}");
            }

            // every public instance property of every config class must have a recorded
            // default above (Events is asserted per event in all-defaults-events)
            var covered = expected.Select(e => e.Path).ToHashSet();
            foreach (var p in typeof(AppConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.Name == "Events") continue; // per-event defaults -> all-defaults-events
                if (p.PropertyType == typeof(QuietHoursConfig) || p.PropertyType == typeof(GitHubConfig) ||
                    p.PropertyType == typeof(CountdownConfig) || p.PropertyType == typeof(FeaturesConfig) ||
                    p.PropertyType == typeof(WindowConfig))
                {
                    foreach (var sp in p.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        if (!covered.Remove(p.Name + "." + sp.Name))
                            problems.Add($"no expected default recorded for {p.Name}.{sp.Name}");
                }
                else if (!covered.Remove(p.Name))
                    problems.Add($"no expected default recorded for {p.Name}");
            }
            foreach (var leftover in covered) // recorded row with no matching property
                problems.Add($"expected-default row '{leftover}' matches no property");

            Check("all-defaults-settings", problems.Count == 0,
                problems.Count == 0 ? $"{expected.Length} settings asserted" : string.Join("; ", problems));
        }
        catch (Exception ex) { Check("all-defaults-settings", false, ex.Message); }

        // 1d. per-event defaults: every registry event present at the fresh-install values.
        // FocusApps is empty in config even where EventRegistry carries DefaultFocusApps -
        // that fallback applies at dispatch time (EventBus), not in the stored config.
        try
        {
            var cfg = new AppConfig();
            AppConfig.EnsureDefaults(cfg);
            var problems = new List<string>();

            foreach (var def in EventRegistry.All)
            {
                if (!cfg.Events.TryGetValue(def.Id, out var ec)) { problems.Add($"{def.Id}: no config entry"); continue; }
                if (ec.Enabled) problems.Add($"{def.Id}.Enabled: expected false, actual true");
                if (ec.Volume != 40) problems.Add($"{def.Id}.Volume: expected 40, actual {ec.Volume}");
                if (ec.SoundPath != "") problems.Add($"{def.Id}.SoundPath: expected empty, actual \"{ec.SoundPath}\"");
                if (ec.FocusApps.Count != 0)
                    problems.Add($"{def.Id}.FocusApps: expected empty, actual [{string.Join(",", ec.FocusApps)}]");
            }
            foreach (var extra in cfg.Events.Keys.Except(EventRegistry.All.Select(d => d.Id)))
                problems.Add($"Events[{extra}]: not in EventRegistry");

            // every persisted per-event property must be asserted above
            var asserted = new HashSet<string> { "Enabled", "Volume", "SoundPath", "FocusApps" };
            var fields = asserted.Count;
            foreach (var p in typeof(EventConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (!asserted.Remove(p.Name))
                    problems.Add($"EventConfig.{p.Name}: no default assertion recorded");
            foreach (var leftover in asserted)
                problems.Add($"EventConfig.{leftover}: asserted but no such property");

            Check("all-defaults-events", problems.Count == 0,
                problems.Count == 0 ? $"{EventRegistry.All.Length} events x {fields} fields asserted" : string.Join("; ", problems));
        }
        catch (Exception ex) { Check("all-defaults-events", false, ex.Message); }

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

        // 3b. SetupRunner: one idempotent command wires the hooks and exactly the core events
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "noizes-selftest-su-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var claudePath = Path.Combine(dir, "claude", "settings.json");
            var cursorPath = Path.Combine(dir, "cursor", "hooks.json");
            var configPath = Path.Combine(dir, "config.json");

            var exit = SetupRunner.Run(new SetupRunner.Options
            {
                ClaudePath = claudePath,
                CursorPath = cursorPath,
                ConfigPath = configPath,
            });

            var expected = SetupRunner.CoreEvents.OrderBy(x => x).ToList();
            List<string> EnabledIds(AppConfig c) =>
                c.Events.Where(kv => kv.Value.Enabled).Select(kv => kv.Key).OrderBy(x => x).ToList();

            var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath));
            var on = cfg == null ? new List<string>() : EnabledIds(cfg);
            var coreAt40 = cfg != null && SetupRunner.CoreEvents
                .All(id => cfg.Events[id].Enabled && cfg.Events[id].Volume == 40);
            var hooks = File.ReadAllText(claudePath).Contains("claude-code-done") &&
                        File.ReadAllText(cursorPath).Contains("cursor-done");
            Check("setup-runner", exit == 0 && on.SequenceEqual(expected) && coreAt40 && hooks,
                $"exit={exit} enabled=[{string.Join(",", on)}]");

            // run twice: idempotent end to end - hooks keep exactly one entry per hook
            // (no duplicates, no new backups), exactly the four core events are on at 40,
            // and the second saved config is semantically identical to the first's
            // (parsed JSON, not bytes)
            var configAfterRun1 = File.ReadAllText(configPath);
            var backupsAfterRun1 = Directory.GetFiles(dir, "*.noizes-backup-*").Length;

            var exit2 = SetupRunner.Run(new SetupRunner.Options
            {
                ClaudePath = claudePath,
                CursorPath = cursorPath,
                ConfigPath = configPath,
            });
            var cfg2 = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath));
            var on2 = cfg2 == null ? new List<string>() : EnabledIds(cfg2);

            // the unique event URLs are the hook entries - each must appear exactly once
            var claudeText = File.ReadAllText(claudePath);
            var cursorText = File.ReadAllText(cursorPath);
            var occClaudeDone = claudeText.Split("/event/claude-code-done").Length - 1;
            var occClaudeInput = claudeText.Split("/event/claude-code-input").Length - 1;
            var occCursorDone = cursorText.Split("/event/cursor-done").Length - 1;

            var coreOnAt40 = cfg2 != null && SetupRunner.CoreEvents
                .All(id => cfg2.Events[id].Enabled && cfg2.Events[id].Volume == 40);
            var restUntouched = cfg2 != null && cfg2.Events
                .Where(kv => !SetupRunner.CoreEvents.Contains(kv.Key))
                .All(kv => !kv.Value.Enabled && kv.Value.Volume == 40);

            var node1 = JsonNode.Parse(configAfterRun1);
            var node2 = JsonNode.Parse(File.ReadAllText(configPath));
            var sameConfig = node1 != null && node2 != null && JsonNode.DeepEquals(node1, node2);

            // a second run must not touch the hook files again ("already" short-circuits
            // before the backup step), so no backup can appear between the runs
            var noNewBackups = Directory.GetFiles(dir, "*.noizes-backup-*").Length == backupsAfterRun1;

            Check("setup-idempotent", exit2 == 0 && on2.SequenceEqual(expected)
                    && occClaudeDone == 1 && occClaudeInput == 1 && occCursorDone == 1
                    && coreOnAt40 && restUntouched && sameConfig && noNewBackups,
                $"exit={exit2} claude={occClaudeDone}/{occClaudeInput} cursor={occCursorDone} " +
                $"sameConfig={sameConfig} enabled=[{string.Join(",", on2)}]");
        }
        catch (Exception ex) { Check("setup-runner", false, ex.Message); }

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

        // 5b. port already taken: the server must fail gracefully instead of crashing startup
        try
        {
            var blockerPort = FreePort();
            var blocker = new HttpServer();
            blocker.Start(blockerPort);

            var victim = new HttpServer();
            Exception busy = null;
            try { victim.Start(blockerPort); }
            catch (Exception ex) { busy = ex; }
            Check("http-port-busy", busy == null && !victim.IsListening,
                busy == null && !victim.IsListening ? "busy port handled, server not listening"
                    : $"threw={busy?.GetType().Name} listening={victim.IsListening}");

            blocker.Stop();
        }
        catch (Exception ex) { Check("http-port-busy", false, ex.Message); }

        // 5c. odd event ids: the JSON response body must stay valid JSON
        try
        {
            var p = FreePort();
            var rawSrv = new HttpServer { DryRun = true };
            rawSrv.Start(p);

            var respText = RawRequest(p, "GET /event/foo\"bar\\baz HTTP/1.1\r\nHost: x\r\n\r\n");
            var body = respText.Substring(respText.IndexOf("\r\n\r\n") + 4);
            var validJson = false;
            var jerr = "";
            try
            {
                using var doc = JsonDocument.Parse(body);
                validJson = doc.RootElement.GetProperty("event").GetString() == "foo\"bar\\baz";
            }
            catch (Exception jex) { jerr = jex.Message; }
            Check("http-json-escaping", validJson,
                validJson ? "404 body is valid JSON and round-trips the id" : "body: " + body + " (" + jerr + ")");

            rawSrv.Stop();
        }
        catch (Exception ex) { Check("http-json-escaping", false, ex.Message); }

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

        // 7. skip-reason logging: every suppressed dispatch explains itself
        try
        {
            var prop = typeof(AppConfig).GetProperty(nameof(AppConfig.Current))!;
            var original = (AppConfig)prop.GetValue(null)!;
            var captured = new List<string>();
            Logger.Sink = captured.Add;
            try
            {
                // fresh all-off config -> the disabled skip must explain itself
                var cfg = new AppConfig();
                AppConfig.EnsureDefaults(cfg);
                prop.GetSetMethod(true)!.Invoke(null, new object[] { cfg });
                EventBus.Dispatch("claude-code-done");
                var disabled = captured.Contains("event claude-code-done: skipped (disabled)");

                // event on + quiet window centered on the current time -> the quiet-hours
                // skip must explain itself (a fixed 00:00-23:59 window misses 23:59:xx)
                captured.Clear();
                cfg.Events["claude-code-done"].Enabled = true;
                var now = DateTime.Now.TimeOfDay;
                string wrap(TimeSpan t)
                {
                    if (t < TimeSpan.Zero) t += TimeSpan.FromHours(24);
                    if (t >= TimeSpan.FromHours(24)) t -= TimeSpan.FromHours(24);
                    return t.ToString(@"hh\:mm\:ss");
                }
                cfg.Quiet.Enabled = true;
                cfg.Quiet.Start = wrap(now - TimeSpan.FromHours(1));
                cfg.Quiet.End = wrap(now + TimeSpan.FromHours(1));
                EventBus.Dispatch("claude-code-done");
                var quiet = captured.Contains("event claude-code-done: skipped (quiet-hours)");

                Check("skip-logging", disabled && quiet,
                    disabled && quiet ? "disabled + quiet-hours lines captured"
                        : $"disabled={disabled} quiet={quiet} captured: {string.Join(" | ", captured)}");
            }
            finally
            {
                Logger.Sink = null;
                prop.GetSetMethod(true)!.Invoke(null, new object[] { original });
            }
        }
        catch (Exception ex) { Check("skip-logging", false, ex.Message); }

        // 8. repo feeds: events by other actors (GitHub Apps, collaborators) must fire
        try
        {
            var prop = typeof(AppConfig).GetProperty(nameof(AppConfig.Current))!;
            var original = (AppConfig)prop.GetValue(null)!;
            var cfg = new AppConfig();
            AppConfig.EnsureDefaults(cfg);
            cfg.GitHub.Token = "t";
            cfg.GitHub.Username = "dennycrafter";
            cfg.GitHub.Repos.Add("dennycrafter/Noizes");
            cfg.GitHub.Repos.Add("not-a-repo"); // invalid entries must never reach the API
            prop.GetSetMethod(true)!.Invoke(null, new object[] { cfg });

            static string Ev(string id, string actor) =>
                "{\"id\":\"" + id + "\",\"type\":\"PushEvent\",\"actor\":{\"login\":\"" + actor +
                "\"},\"repo\":{\"name\":\"dennycrafter/Noizes\"},\"payload\":{\"size\":1}}";

            static HttpResponseMessage Resp(int code, string body, string etag)
            {
                var r = new HttpResponseMessage((System.Net.HttpStatusCode)code)
                    { Content = new StringContent(body) };
                if (etag != null) r.Headers.ETag = new EntityTagHeaderValue(etag);
                return r;
            }

            var requests = new List<string>();
            var cycle = 0;
            var poller = new GitHubPoller();
            poller.TestHttp = req =>
            {
                var url = req.RequestUri.PathAndQuery;
                var inm = req.Headers.IfNoneMatch.Count > 0 ? req.Headers.IfNoneMatch.First().ToString() : "";
                requests.Add($"{url} inm={inm}");
                if (url.Contains("/users/"))
                    return cycle == 1
                        ? Resp(200, "[" + Ev("user-ev-1", "dennycrafter") + "]", "\"u1\"")
                        : Resp(304, "[]", null);
                return cycle switch
                {
                    1 => Resp(200, "[" + Ev("repo-old-1", "somecollab") + "]", "\"r1\""),
                    2 => Resp(200, "[" + Ev("user-ev-1", "dennycrafter") + "]", "\"r1\""),
                    _ => Resp(200, "[" + Ev("bot-ev-3", "obvious[bot]") + "]", "\"r1\""),
                };
            };

            var captured = new List<string>();
            Logger.Sink = captured.Add;
            try
            {
                int Pushes() => captured.Count(l => l.Contains("github event: gh-push on dennycrafter/Noizes"));

                cycle = 1; poller.PollOnce().Wait(); var c1 = Pushes(); // prime: history from both feeds
                cycle = 2; poller.PollOnce().Wait(); var c2 = Pushes() - c1; // repo feed replays user-ev-1
                cycle = 3; poller.PollOnce().Wait(); var c3 = Pushes() - c1 - c2; // push by obvious[bot] arrives

                Check("repo-feed-dedupe", c1 == 0 && c2 == 0,
                    $"prime={c1} replay={c2} - an id seen via the user feed must not re-fire from the repo feed");
                Check("repo-feed-other-actor", c3 == 1,
                    $"botPush={c3} - PushEvent by obvious[bot] must fire gh-push");
                Check("repo-feed-etag",
                    requests.Any(r => r.StartsWith("/users/dennycrafter/events") && r.Contains("\"u1\"")) &&
                    requests.Any(r => r.StartsWith("/repos/dennycrafter/Noizes/events") && r.Contains("\"r1\"")) &&
                    !requests.Any(r => r.Contains("not-a-repo")),
                    string.Join(" | ", requests));
                Check("repo-list-normalizes",
                    GitHubPoller.NormalizeRepo(" https://github.com/dennycrafter/Noizes.git ") == "dennycrafter/Noizes" &&
                    GitHubPoller.NormalizeRepo("dennycrafter/Noizes/") == "dennycrafter/Noizes" &&
                    GitHubPoller.NormalizeRepo("no-slash") == null &&
                    GitHubPoller.NormalizeRepo("a/b/c") == null &&
                    GitHubPoller.NormalizeRepo("") == null);
            }
            finally
            {
                Logger.Sink = null;
                prop.GetSetMethod(true)!.Invoke(null, new object[] { original });
            }
        }
        catch (Exception ex) { Check("repo-feed-other-actor", false, ex.Message); }

        // 7b. features flag: old configs without the group default to watcher-off; the flag roundtrips
        try
        {
            var loaded = JsonSerializer.Deserialize<AppConfig>("{}");
            Check("features-default-off", loaded?.Features != null && !loaded.Features.ClaudeDesktopWatcher);

            var withFlag = JsonSerializer.Deserialize<AppConfig>("{\"Features\":{\"ClaudeDesktopWatcher\":true}}");
            Check("features-roundtrip", withFlag?.Features.ClaudeDesktopWatcher == true);

            var reloaded = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(withFlag));
            Check("features-save-reload", reloaded?.Features.ClaudeDesktopWatcher == true);
        }
        catch (Exception ex) { Check("features-default-off", false, ex.Message); }


        // 7c. a duplicate trigger inside the dedup window must not be mislabeled sound-missing
        try
        {
            var prop = typeof(AppConfig).GetProperty(nameof(AppConfig.Current))!;
            var original = (AppConfig)prop.GetValue(null)!;
            var captured = new List<string>();
            Logger.Sink = captured.Add;
            try
            {
                var cfg = new AppConfig();
                AppConfig.EnsureDefaults(cfg);
                cfg.Events["claude-code-done"].Enabled = true;
                prop.GetSetMethod(true)!.Invoke(null, new object[] { cfg });

                EventBus.Dispatch("claude-code-done"); // first fire: played (file exists, harness has the sounds)
                EventBus.Dispatch("claude-code-done"); // second fire: inside the 1.2 s dedup window

                var dedup = captured.Count(l => l.Contains(": deduplicated ("));
                var missing = captured.Count(l => l.Contains(": sound-missing ("));
                Check("dedup-reason", dedup == 1 && missing == 0,
                    dedup == 1 && missing == 0 ? "second fire explains itself as deduplicated"
                        : "captured: " + string.Join(" | ", captured));
            }
            finally
            {
                Logger.Sink = null;
                prop.GetSetMethod(true)!.Invoke(null, new object[] { original });
            }
        }
        catch (Exception ex) { Check("dedup-reason", false, ex.Message); }

        // 10. v1.2.0 compatibility: a config.json from v1.2.0 (no Muted/MutedUntil/Window)
        // loads with every old value unchanged and the new fields at their safe defaults
        try
        {
            const string v120 = """
                {
                  "Port": 7351,
                  "StartWithWindows": true,
                  "OnlyWhenUnfocused": true,
                  "Quiet": { "Enabled": true, "Start": "23:00", "End": "06:30", "AllowAlarms": false },
                  "GitHub": { "Token": "tok123", "Username": "den", "PollSeconds": 30, "Repos": ["a/b"] },
                  "UptimeUrls": [ "https://example.com" ],
                  "Countdown": { "Enabled": true, "TargetLocal": "2026-12-01T12:00:00" },
                  "Features": { "ClaudeDesktopWatcher": true },
                  "Events": { "claude-code-done": { "Enabled": true, "Volume": 65, "SoundPath": "C:\\sounds\\win.mp3", "FocusApps": ["code"] } }
                }
                """;
            var loaded = JsonSerializer.Deserialize<AppConfig>(v120);
            AppConfig.EnsureDefaults(loaded);
            var ec = loaded.Events["claude-code-done"];
            var oldOk = loaded.Port == 7351 && loaded.StartWithWindows && loaded.OnlyWhenUnfocused &&
                loaded.Quiet.Enabled && loaded.Quiet.Start == "23:00" && loaded.Quiet.End == "06:30" && !loaded.Quiet.AllowAlarms &&
                loaded.GitHub.Token == "tok123" && loaded.GitHub.Username == "den" && loaded.GitHub.PollSeconds == 30 &&
                loaded.GitHub.Repos.SequenceEqual(new[] { "a/b" }) &&
                loaded.UptimeUrls.SequenceEqual(new[] { "https://example.com" }) &&
                loaded.Countdown.Enabled && loaded.Countdown.TargetLocal == new DateTime(2026, 12, 1, 12, 0, 0) &&
                loaded.Features.ClaudeDesktopWatcher && ec.Enabled && ec.Volume == 65 &&
                ec.SoundPath == "C:\\sounds\\win.mp3" && ec.FocusApps.SequenceEqual(new[] { "code" });
            var newOk = !loaded.Muted && loaded.MutedUntil == null &&
                loaded.Window != null && loaded.Window.X == -1 && loaded.Window.Y == -1 &&
                loaded.Window.Width == 1080 && loaded.Window.Height == 720 && !loaded.Window.Maximized;
            Check("v120-config-survives", oldOk && newOk,
                oldOk && newOk ? "old values unchanged, new fields at safe defaults" : $"oldOk={oldOk} newOk={newOk}");
        }
        catch (Exception ex) { Check("v120-config-survives", false, ex.Message); }

        // 11. mute: the dispatch gate skips with reason muted, the uptime alarm obeys it,
        // a timed mute works, and a passed MutedUntil clears itself
        try
        {
            var prop = typeof(AppConfig).GetProperty(nameof(AppConfig.Current))!;
            var original = (AppConfig)prop.GetValue(null)!;
            AppConfig.DirOverride = Path.Combine(Path.GetTempPath(), "noizes-selftest-mute-" + Guid.NewGuid().ToString("N"));
            var captured = new List<string>();
            Logger.Sink = captured.Add;
            try
            {
                var cfg = new AppConfig();
                AppConfig.EnsureDefaults(cfg);
                cfg.Events["claude-code-done"].Enabled = true;
                prop.GetSetMethod(true)!.Invoke(null, new object[] { cfg });

                cfg.Muted = true;
                var r1 = EventBus.Dispatch("claude-code-done");
                var logged = captured.Contains("event claude-code-done: skipped (muted)");
                Check("mute-skips", r1.Reason == "muted" && logged,
                    $"reason={r1.Reason} logged={logged}");

                // the uptime alarm obeys mute too, even though quiet hours would exempt it
                var r2 = EventBus.Dispatch("uptime-alarm");
                Check("mute-gates-alarm", r2.Reason == "muted", $"reason={r2.Reason}");

                // a timed mute in the future silences as well
                cfg.Muted = false;
                cfg.MutedUntil = DateTime.Now.AddMinutes(5);
                var r3 = EventBus.Dispatch("claude-code-done");
                Check("timed-mute-active", r3.Reason == "muted", $"reason={r3.Reason}");

                // a MutedUntil in the past clears itself and unmutes the gate
                cfg.MutedUntil = DateTime.Now.AddMinutes(-5);
                var r4 = EventBus.Dispatch("claude-code-done");
                Check("muted-until-clears", cfg.MutedUntil == null && r4.Reason != "muted",
                    $"cleared={cfg.MutedUntil == null} reason={r4.Reason}");

                // and IsMutedNow agrees with the gate from the outside
                cfg.MutedUntil = DateTime.Now.AddMinutes(5);
                var nowActive = EventBus.IsMutedNow(cfg);
                cfg.MutedUntil = null;
                Check("mute-state-helper", nowActive && !EventBus.IsMutedNow(cfg), "pure check matches the gate");
            }
            finally
            {
                Logger.Sink = null;
                AppConfig.DirOverride = null;
                prop.GetSetMethod(true)!.Invoke(null, new object[] { original });
            }
        }
        catch (Exception ex) { Check("mute-skips", false, ex.Message); }

        // 12. one source of truth: the Claude desktop event switch and
        // Features.ClaudeDesktopWatcher move together, and the watcher is told to run or stop
        try
        {
            var prop = typeof(AppConfig).GetProperty(nameof(AppConfig.Current))!;
            var original = (AppConfig)prop.GetValue(null)!;
            AppConfig.DirOverride = Path.Combine(Path.GetTempPath(), "noizes-selftest-sync-" + Guid.NewGuid().ToString("N"));
            try
            {
                var cfg = new AppConfig();
                AppConfig.EnsureDefaults(cfg);
                prop.GetSetMethod(true)!.Invoke(null, new object[] { cfg });

                bool? watcherTold = null;
                UiBridge.Services.RunClaudeDesktopWatcher = b => watcherTold = b;

                UiBridge.SetEventEnabled("claude-desktop-done", true);
                var onOk = cfg.Events["claude-desktop-done"].Enabled && cfg.Features.ClaudeDesktopWatcher && watcherTold == true;
                Check("claude-sync-on", onOk, $"event={cfg.Events["claude-desktop-done"].Enabled} flag={cfg.Features.ClaudeDesktopWatcher} watcherTold={watcherTold}");

                UiBridge.SetEventEnabled("claude-desktop-done", false);
                var offOk = !cfg.Events["claude-desktop-done"].Enabled && !cfg.Features.ClaudeDesktopWatcher && watcherTold == false;
                Check("claude-sync-off", offOk, $"event={cfg.Events["claude-desktop-done"].Enabled} flag={cfg.Features.ClaudeDesktopWatcher} watcherTold={watcherTold}");

                // the other direction: the Connections page switch moves the event too
                UiBridge.SetWatcherEnabled(true);
                Check("claude-sync-reverse", cfg.Features.ClaudeDesktopWatcher && cfg.Events["claude-desktop-done"].Enabled,
                    "flag on turned the event on");

                // a normal event switch must not touch the watcher flag
                UiBridge.SetEventEnabled("claude-code-done", true);
                Check("claude-sync-untouched-elsewhere",
                    cfg.Events["claude-code-done"].Enabled && cfg.Features.ClaudeDesktopWatcher,
                    "unrelated event left the flag alone");

                // the saved file shows the same state
                var saved = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(AppConfig.FilePath));
                Check("claude-sync-persisted",
                    saved != null && saved.Features.ClaudeDesktopWatcher &&
                    saved.Events["claude-desktop-done"].Enabled,
                    "saved config agrees with memory");
            }
            finally
            {
                UiBridge.Services.RunClaudeDesktopWatcher = null;
                AppConfig.DirOverride = null;
                prop.GetSetMethod(true)!.Invoke(null, new object[] { original });
            }
        }
        catch (Exception ex) { Check("claude-sync-on", false, ex.Message); }

        // 13. bridge envelope: replies carry {id, ok, data}, the token never leaves the app,
        // and a volume-only change defers its save to the 300ms debounce
        try
        {
            var prop = typeof(AppConfig).GetProperty(nameof(AppConfig.Current))!;
            var original = (AppConfig)prop.GetValue(null)!;
            AppConfig.DirOverride = Path.Combine(Path.GetTempPath(), "noizes-selftest-bridge-" + Guid.NewGuid().ToString("N"));
            try
            {
                var cfg = new AppConfig();
                AppConfig.EnsureDefaults(cfg);
                cfg.GitHub.Token = "secret-token-xyz";
                prop.GetSetMethod(true)!.Invoke(null, new object[] { cfg });

                var posted = new List<string>();
                Action<string> probe = posted.Add;
                UiBridge.Attach(probe);
                try
                {
                    UiBridge.HandleMessage("{\"id\":7,\"type\":\"getState\"}");
                    var reply = posted.FirstOrDefault(l => l.Contains("\"id\":7"));
                    var shaped = reply != null && reply.Contains("\"ok\":true") && reply.Contains("\"tokenSet\":true");
                    Check("envelope-shape", shaped,
                        shaped ? "getState replied {id, ok, data} with tokenSet" : "reply: " + (reply ?? "none"));
                    Check("token-never-sent", reply != null && !reply.Contains("secret-token-xyz"),
                        "the token value stayed in the app");

                    // an unknown type and an unknown event id answer with ok:false, not silence
                    UiBridge.HandleMessage("{\"id\":8,\"type\":\"no-such-message\"}");
                    UiBridge.HandleMessage("{\"id\":9,\"type\":\"setEvent\",\"eventId\":\"not-an-event\",\"enabled\":true}");
                    Check("envelope-errors",
                        posted.Any(l => l.Contains("\"id\":8") && l.Contains("\"ok\":false")) &&
                        posted.Any(l => l.Contains("\"id\":9") && l.Contains("\"ok\":false")),
                        "unknown type and unknown event both answered");
                }
                finally { UiBridge.Detach(probe); }

                // volume-only: the reply goes out at once but the file save is deferred
                UiBridge.SetEventEnabled("claude-code-done", true); // immediate save at volume 40
                var before = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(AppConfig.FilePath));
                UiBridge.HandleMessage("{\"id\":10,\"type\":\"setEvent\",\"eventId\":\"claude-code-done\",\"volume\":77}");
                var afterMsg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(AppConfig.FilePath));
                var deferred = afterMsg != null && afterMsg.Events["claude-code-done"].Volume == before.Events["claude-code-done"].Volume;
                UiBridge.FlushPendingSave();
                var flushed = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(AppConfig.FilePath));
                Check("volume-save-debounced", deferred && flushed.Events["claude-code-done"].Volume == 77,
                    $"deferred={deferred} flushed={flushed.Events["claude-code-done"].Volume}");
            }
            finally
            {
                AppConfig.DirOverride = null;
                prop.GetSetMethod(true)!.Invoke(null, new object[] { original });
            }
        }
        catch (Exception ex) { Check("envelope-shape", false, ex.Message); }

        // 14. page messages reach the bridge whatever form WebView2 hands them over in. The page
        // posts objects; reading them with TryGetWebMessageAsString threw and lost every request,
        // which left the window at "Loading settings." in v1.3.0-beta.2.
        try
        {
            const string asObject = "{\"id\":1,\"type\":\"getState\"}";
            const string asString = "\"{\\\"id\\\":1,\\\"type\\\":\\\"getState\\\"}\"";
            Check("message-object-read", UiBridge.DecodeWebMessage(asObject) == asObject, asObject);
            var unwrapped = UiBridge.DecodeWebMessage(asString);
            Check("message-string-read", unwrapped == asObject, "got " + (unwrapped ?? "null"));
            Check("message-empty-ignored", UiBridge.DecodeWebMessage("") == null && UiBridge.DecodeWebMessage("\"not json") == null);

            // the whole path: an object message as WebView2 delivers it gets a getState reply
            var posted = new List<string>();
            Action<string> probe = posted.Add;
            UiBridge.Attach(probe);
            try { UiBridge.HandleMessage(UiBridge.DecodeWebMessage("{\"id\":41,\"type\":\"getState\"}")); }
            finally { UiBridge.Detach(probe); }
            Check("message-object-answered", posted.Any(l => l.Contains("\"id\":41") && l.Contains("\"ok\":true")),
                posted.Count + " replies");
        }
        catch (Exception ex) { Check("message-object-read", false, ex.Message); }

        // 15. the settings window always opens fully on a screen
        try
        {
            var main = new System.Drawing.Rectangle(0, 0, 1920, 1040);   // 1080p minus the taskbar
            var left = new System.Drawing.Rectangle(-1920, 0, 1920, 1040); // a second monitor on the left
            var small = new System.Drawing.Rectangle(0, 0, 1366, 728);
            var def = new System.Drawing.Size(1080, 720);
            bool Inside(System.Drawing.Rectangle r, System.Drawing.Rectangle a) => a.Contains(r);

            var fresh = SettingsWindow.PlaceWindow(new System.Drawing.Rectangle(-1, -1, 1080, 720), new[] { main }, main, def);
            Check("place-first-run-centered", Inside(fresh, main) && fresh.X == 420 && fresh.Y == 160, fresh.ToString());

            var kept = SettingsWindow.PlaceWindow(new System.Drawing.Rectangle(200, 100, 1000, 700), new[] { main }, main, def);
            Check("place-saved-kept", kept == new System.Drawing.Rectangle(200, 100, 1000, 700), kept.ToString());

            var gone = SettingsWindow.PlaceWindow(new System.Drawing.Rectangle(-1700, 50, 1080, 720), new[] { main }, main, def);
            Check("place-monitor-unplugged", Inside(gone, main), gone.ToString());

            var other = SettingsWindow.PlaceWindow(new System.Drawing.Rectangle(-1700, 50, 1080, 720), new[] { main, left }, main, def);
            Check("place-second-monitor-kept", other.X == -1700 && other.Y == 50, other.ToString());

            var far = SettingsWindow.PlaceWindow(new System.Drawing.Rectangle(-32000, -32000, 1080, 720), new[] { main }, main, def);
            Check("place-far-off-screen", Inside(far, main), far.ToString());

            var edge = SettingsWindow.PlaceWindow(new System.Drawing.Rectangle(1500, 600, 1080, 720), new[] { main }, main, def);
            Check("place-hanging-off-edge", Inside(edge, main), edge.ToString());

            var huge = SettingsWindow.PlaceWindow(new System.Drawing.Rectangle(0, 0, 4000, 3000), new[] { small }, small, def);
            Check("place-bigger-than-screen", Inside(huge, small), huge.ToString());

            var junk = SettingsWindow.PlaceWindow(new System.Drawing.Rectangle(5, 5, 0, 0), new[] { main }, main, def);
            Check("place-junk-size", Inside(junk, main) && junk.Width == 1080, junk.ToString());

            var none = SettingsWindow.PlaceWindow(new System.Drawing.Rectangle(-1, -1, 1080, 720), Array.Empty<System.Drawing.Rectangle>(), System.Drawing.Rectangle.Empty, def);
            Check("place-no-screen-info", none.Width > 0 && none.Height > 0, none.ToString());
        }
        catch (Exception ex) { Check("place-first-run-centered", false, ex.Message); }

        // 16. Claude desktop watcher: a ding only when Claude finishes in the chat that is open.
        // Button names below are the real ones captured from the Claude app on 14 Oct 2026.
        try
        {
            var working = ClaudeDesktopWatcher.ReadWindow(new[] {
                "Activity panel", "Stop response", "Noizes repo fixes and PR, rename session", "Send message" });
            Check("watch-read-working", working.StopVisible && working.Chat == "Noizes repo fixes and PR",
                $"stop={working.StopVisible} chat={working.Chat}");
            var idle = ClaudeDesktopWatcher.ReadWindow(new[] {
                "Send message", "Use voice mode", "Noizes repo fixes and PR, rename session", "Ran 3 commands" });
            Check("watch-read-idle", !idle.StopVisible && idle.Chat == "Noizes repo fixes and PR",
                $"stop={idle.StopVisible} chat={idle.Chat}");
            var other = ClaudeDesktopWatcher.ReadWindow(new[] { "Chrome bookmarks review, rename session", "Send message" });
            Check("watch-read-other-chat", !other.StopVisible && other.Chat == "Chrome bookmarks review", $"chat={other.Chat}");
            var legacy = ClaudeDesktopWatcher.ReadWindow(new[] { "Stop" });
            Check("watch-read-old-app", legacy.StopVisible && legacy.Chat == null, $"chat={legacy.Chat ?? "none"}");

            const string chat = "Noizes repo fixes and PR";
            Check("watch-finish-same-chat",
                ClaudeDesktopWatcher.Decide(true, chat, false, chat) == ClaudeDesktopWatcher.Step.Finished,
                "Stop gone, same chat open: ding");
            Check("watch-switch-no-ding",
                ClaudeDesktopWatcher.Decide(true, chat, false, "Chrome bookmarks review") == ClaudeDesktopWatcher.Step.SwitchedAway,
                "Stop gone because another chat was opened: no ding (the beta.3 early ding)");
            Check("watch-window-gone-no-ding",
                ClaudeDesktopWatcher.Decide(true, chat, false, null) == ClaudeDesktopWatcher.Step.SwitchedAway,
                "Claude window closed or unreadable mid-reply: no ding");
            Check("watch-still-working",
                ClaudeDesktopWatcher.Decide(true, chat, true, chat) == ClaudeDesktopWatcher.Step.Working, "Stop still there");
            Check("watch-idle",
                ClaudeDesktopWatcher.Decide(false, null, false, chat) == ClaudeDesktopWatcher.Step.Idle, "nothing running");
            Check("watch-new-chat-named-dings",
                ClaudeDesktopWatcher.Decide(true, "New chat", false, "Tweet to Claude skill") == ClaudeDesktopWatcher.Step.Finished,
                "a new chat renamed as its first reply ends is a finish (missed in v1.3.0, seen on Denis's PC)");
            Check("watch-new-chat-window-gone-no-ding",
                ClaudeDesktopWatcher.Decide(true, "New chat", false, null) == ClaudeDesktopWatcher.Step.SwitchedAway,
                "a new chat whose window closed mid-reply: no ding");
            Check("watch-named-chat-switch-still-quiet",
                ClaudeDesktopWatcher.Decide(true, "Tweet to Claude skill", false, "New chat") == ClaudeDesktopWatcher.Step.SwitchedAway,
                "leaving a named chat for a new one: no ding");
            Check("watch-old-app-still-dings",
                ClaudeDesktopWatcher.Decide(true, null, false, null) == ClaudeDesktopWatcher.Step.Finished,
                "an app without chat names keeps the old Stop-only behavior");
        }
        catch (Exception ex) { Check("watch-read-working", false, ex.Message); }

        // 9. windows CI only: the settings form must construct headless - no Show(), so no
        // window handles (MessageBox lives only in click handlers, never in the ctor).
        // Its own method: the Linux harness never calls it, so the WinForms type graph
        // stays untouched here.
        if (OperatingSystem.IsWindows())
        {
            try { RunWindowsFormChecks(); }
            catch (Exception ex) { Check("form-constructs", false, $"{ex.GetType().Name}: {ex.Message}"); }
        }

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

    // Windows CI only (guarded in Run): construct the settings form headless and assert
    // the whole surface came up - all four tabs present. Runs for real on windows-latest;
    // skipped on Linux where WinForms cannot load. NoInlining keeps the SettingsForm
    // reference out of Run's compiled body on Linux.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void RunWindowsFormChecks()
    {
        // the real app loads config before any form opens (Program.Main); without it the
        // ctor's StyleEventRow hits an empty Events dictionary
        AppConfig.Load();
        using var form = new SettingsForm();
        var tabs = FindDescendant(form, c => c is TabControl) as TabControl;
        Check("form-constructs", form.Controls.Count > 0 && tabs != null && tabs.TabCount == 4,
            $"controls={form.Controls.Count} tabs={(tabs == null ? -1 : tabs.TabCount)}");
        // every registry category must render as a group in the events list
        var list = FindDescendant(form, c => c is ListView) as ListView;
        var expectedGroups = EventRegistry.All.Select(d => d.Category).Distinct().Count();
        Check("event-groups-render", list != null && list.Groups.Count == expectedGroups,
            $"groups={(list == null ? -1 : list.Groups.Count)} expected={expectedGroups}");
    }

    static Control FindDescendant(Control root, Func<Control, bool> match)
    {
        foreach (Control child in root.Controls)
        {
            if (match(child)) return child;
            var found = FindDescendant(child, match);
            if (found != null) return found;
        }
        return null;
    }
}

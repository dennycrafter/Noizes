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
                    p.PropertyType == typeof(CountdownConfig) || p.PropertyType == typeof(FeaturesConfig))
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

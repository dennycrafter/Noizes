using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Net.Http.Headers;
using System.Text;
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

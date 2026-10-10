using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows.Forms;
using Timer = System.Threading.Timer;

namespace Noizes;

/// <summary>
/// The WebView2 message bridge: the settings page talks to C# only through here
/// (window.chrome.webview.postMessage in, PostWebMessageAsJson out). Config is never
/// exposed over HTTP: the port 7351 server stays integration-only, so no website can
/// read or write settings.
///
/// Message contract (page to app): a JSON object {id, type, ...params} posted via
/// window.chrome.webview.postMessage. Every request gets exactly one reply
/// {id, ok, data?} or {id, ok: false, error}.
/// App to page push: {type: "state", data: stateObject} whenever anything changes
/// outside the page (tray mute, setup finished, GitHub status changed). The state
/// object is the same one getState replies with, so the page renders both alike.
///
/// Threading: the window host must call HandleMessage on the UI thread (chooseSound
/// opens a modal file picker) and wrap Attach's callback in a BeginInvoke so posts
/// from background threads marshal before they reach the WebView2.
/// </summary>
public static class UiBridge
{
    /// <summary>The event whose switch and the watcher flag are one and the same control.</summary>
    public const string ClaudeDesktopEventId = "claude-desktop-done";

    static Action<string> _post; // raw JSON to the page; host-provided, marshals to the UI thread

    /// <summary>Host wiring: the callback posts raw JSON to the page (must marshal to the UI thread).</summary>
    public static void Attach(Action<string> postJsonToPage) => _post = postJsonToPage;

    /// <summary>Host wiring on window close; only clears when the same callback is detached.</summary>
    public static void Detach(Action<string> postJsonToPage)
    {
        if (_post == postJsonToPage) _post = null;
    }

    /// <summary>
    /// Live services the bridge may need to poke; all optional, wired once by the app
    /// host at startup (TrayApplicationContext). The bridge works without them so the
    /// self-test can run headless and substitute its own.
    /// </summary>
    public static class Services
    {
        /// <summary>Start (true) or stop (false) the Claude desktop watcher as the flag changes.</summary>
        public static Action<bool> RunClaudeDesktopWatcher;
        /// <summary>Re-arm the GitHub poller after pollSeconds changes.</summary>
        public static Action RestartGitHubPoller;
        /// <summary>Port the local HTTP server is actually listening on (hooks embed it); null falls back to config.</summary>
        public static Func<int> ListeningPort;
    }

    /// <summary>Raised on every state change for non-page listeners (tray icon, tooltip). Subscribers marshal to the UI thread.</summary>
    public static event Action StateChanged;

    /// <summary>Call after anything changes the config outside the page: pushes state and updates listeners.</summary>
    public static void NotifyStateChanged()
    {
        StateChanged?.Invoke();
        PushState();
    }

    // ---------------------------------------------------------------- messages in

    /// <summary>One message from the page. Must run on the UI thread; see the class comment.</summary>
    public static void HandleMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            Logger.Info("bridge: dropped a message that was not valid JSON");
            return;
        }
        if (root.ValueKind != JsonValueKind.Object) return;

        var rawId = root.TryGetProperty("id", out var idEl) ? idEl.GetRawText() : "null";
        if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
        {
            PostError(rawId, "missing type");
            return;
        }
        var type = typeEl.GetString() ?? "";

        try
        {
            Route(type, root, rawId);
        }
        catch (Exception ex)
        {
            Logger.Info($"bridge: {type} failed: {ex.Message}");
            PostError(rawId, ex.Message);
        }
    }

    static void Route(string type, JsonElement msg, string rawId)
    {
        switch (type)
        {
            case "getState":
                PostReply(rawId, BuildState());
                break;

            case "setEvent": SetEvent(msg, rawId); break;
            case "setGroup": SetGroup(msg, rawId); break;

            case "setMute":
                var muted = Bool(msg, "muted");
                if (!muted.HasValue) { PostError(rawId, "setMute needs muted"); break; }
                SetMuted(muted.Value, Int(msg, "minutes"));
                var c = AppConfig.Current;
                PostReply(rawId, new { muted = EventBus.IsMutedNow(c), mutedUntil = c.MutedUntil });
                break;

            case "testSound": TestSound(msg, rawId); break;
            case "chooseSound": ChooseSound(msg, rawId); break;
            case "resetSound": ResetSound(msg, rawId); break;

            case "setupClaudeCode":
                var claude = ClaudeCodeConnector.Connect(null, HookPort());
                NotifyStateChanged(); // setup finished: the Connections page status follows
                PostReply(rawId, new { ok = claude.ok, message = claude.message });
                break;

            case "setupCursor":
                var cursor = CursorConnector.Connect(null, HookPort());
                NotifyStateChanged();
                PostReply(rawId, new { ok = cursor.ok, message = cursor.message });
                break;

            case "saveGitHub": SaveGitHub(msg, rawId); break;

            case "testGitHub": TestGitHub(rawId); break;

            case "setDesktopWatcher":
                var on = Bool(msg, "enabled");
                if (!on.HasValue) { PostError(rawId, "setDesktopWatcher needs enabled"); break; }
                SetWatcherEnabled(on.Value);
                PostReply(rawId, new { watcherEnabled = AppConfig.Current.Features.ClaudeDesktopWatcher });
                break;

            case "setGeneral": SetGeneral(msg, rawId); break;
            case "setQuietHours": SetQuietHours(msg, rawId); break;
            case "setUptime": SetUptime(msg, rawId); break;
            case "setCountdown": SetCountdown(msg, rawId); break;

            case "openLog":
                Directory.CreateDirectory(AppConfig.Dir);
                if (!File.Exists(Logger.LogPath)) File.WriteAllText(Logger.LogPath, "");
                Process.Start(new ProcessStartInfo { FileName = Logger.LogPath, UseShellExecute = true });
                PostReply(rawId, null);
                break;

            case "openFolder":
                Directory.CreateDirectory(AppConfig.Dir);
                Process.Start(new ProcessStartInfo { FileName = AppConfig.Dir, UseShellExecute = true });
                PostReply(rawId, null);
                break;

            case "openUrl": OpenUrl(msg, rawId); break;

            default:
                PostError(rawId, $"unknown message type: {type}");
                break;
        }
    }

    // ---------------------------------------------------------------- handlers

    static void SetEvent(JsonElement msg, string rawId)
    {
        var eventId = Str(msg, "eventId");
        if (string.IsNullOrEmpty(eventId) || EventRegistry.Get(eventId) == null)
        {
            PostError(rawId, "unknown eventId");
            return;
        }
        var cfg = AppConfig.Current;
        var ec = cfg.Events[eventId]; // EnsureDefaults guarantees the entry

        var enabled = Bool(msg, "enabled");
        var volume = Int(msg, "volume");
        var soundPath = Str(msg, "soundPath");
        var focusApps = StrList(msg, "focusApps");
        bool volumeOnly = volume.HasValue && !enabled.HasValue && soundPath == null && focusApps == null;

        if (enabled.HasValue) ApplyEventEnabled(cfg, eventId, enabled.Value); // Claude sync rides along
        if (volume.HasValue) ec.Volume = Math.Clamp(volume.Value, 0, 100);
        if (soundPath != null) ec.SoundPath = soundPath;
        if (focusApps != null) ec.FocusApps = focusApps;

        if (volumeOnly) SaveVolumeDebounced(); // slider drags coalesce into one save per 300ms
        else AppConfig.Save();

        PostReply(rawId, new { eventId, ec.Enabled, ec.Volume, ec.SoundPath, ec.FocusApps });
    }

    static void SetGroup(JsonElement msg, string rawId)
    {
        var group = Str(msg, "group");
        var enabled = Bool(msg, "enabled");
        if (string.IsNullOrEmpty(group) || !enabled.HasValue)
        {
            PostError(rawId, "setGroup needs group and enabled");
            return;
        }
        var ids = EventRegistry.All.Where(d => d.Category == group).Select(d => d.Id).ToList();
        if (ids.Count == 0)
        {
            PostError(rawId, "unknown group: " + group);
            return;
        }
        var cfg = AppConfig.Current;
        foreach (var id in ids) ApplyEventEnabled(cfg, id, enabled.Value);
        AppConfig.Save();
        PostReply(rawId, new { group, enabled = enabled.Value, count = ids.Count });
    }

    static void TestSound(JsonElement msg, string rawId)
    {
        var eventId = Str(msg, "eventId");
        var def = EventRegistry.Get(eventId);
        if (def == null)
        {
            PostError(rawId, "unknown eventId");
            return;
        }
        var cfg = AppConfig.Current;
        var ec = cfg.Events.TryGetValue(eventId, out var e) ? e : new EventConfig();
        var path = ec.SoundPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            path = Path.Combine(AppConfig.SoundsDir, def.DefaultSound);
        // an explicit Test is a preview: it plays even while muted, because mute gates
        // automatic playback only and never the button the owner just pressed
        var play = AudioPlayer.Play(eventId, path, ec.Volume);
        Logger.Info($"event {eventId}: test ({play.Reason})");
        PostReply(rawId, new { played = play.Played, reason = play.Reason });
    }

    static void ChooseSound(JsonElement msg, string rawId)
    {
        var eventId = Str(msg, "eventId");
        if (string.IsNullOrEmpty(eventId) || EventRegistry.Get(eventId) == null ||
            !AppConfig.Current.Events.ContainsKey(eventId))
        {
            PostError(rawId, "unknown eventId");
            return;
        }
        using var dlg = new OpenFileDialog
        {
            Title = "Choose a sound",
            Filter = "Sounds (*.wav;*.mp3)|*.wav;*.mp3",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != DialogResult.OK)
        {
            PostReply(rawId, new { path = (string)null, cancelled = true });
            return;
        }
        var ec = AppConfig.Current.Events[eventId];
        ec.SoundPath = dlg.FileName;
        AppConfig.Save();
        PostReply(rawId, new { path = dlg.FileName, cancelled = false });
    }

    static void ResetSound(JsonElement msg, string rawId)
    {
        var eventId = Str(msg, "eventId");
        if (string.IsNullOrEmpty(eventId) || !AppConfig.Current.Events.TryGetValue(eventId, out var ec))
        {
            PostError(rawId, "unknown eventId");
            return;
        }
        ec.SoundPath = "";
        AppConfig.Save();
        PostReply(rawId, new { eventId, ec.SoundPath });
    }

    static void SaveGitHub(JsonElement msg, string rawId)
    {
        var cfg = AppConfig.Current;
        var token = Str(msg, "token");
        if (!string.IsNullOrWhiteSpace(token)) cfg.GitHub.Token = token.Trim(); // absent or empty keeps the stored token
        var username = Str(msg, "username");
        if (username != null) cfg.GitHub.Username = username.Trim();
        var poll = Int(msg, "pollSeconds");
        if (poll.HasValue) cfg.GitHub.PollSeconds = Math.Clamp(poll.Value, 5, 300);
        var repos = StrList(msg, "repos");
        if (repos != null) cfg.GitHub.Repos = repos;

        AppConfig.Save();
        Services.RestartGitHubPoller?.Invoke();
        NotifyStateChanged(); // GitHub status changed
        PostReply(rawId, GithubState());
    }

    static void TestGitHub(string rawId)
    {
        var token = AppConfig.Current.GitHub.Token;
        if (string.IsNullOrWhiteSpace(token))
        {
            PostError(rawId, "No token saved yet. Paste one and press Save first.");
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
                req.Headers.UserAgent.ParseAdd("Noizes/1.0");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var resp = await http.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();
                if ((int)resp.StatusCode == 200)
                {
                    var login = "";
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        login = doc.RootElement.GetProperty("login").GetString() ?? "";
                    }
                    catch { }
                    PostReply(rawId, new { login });
                }
                else
                {
                    PostError(rawId, $"GitHub said {(int)resp.StatusCode}. Check the token.");
                }
            }
            catch (Exception ex)
            {
                PostError(rawId, "Could not reach GitHub: " + ex.Message);
            }
        });
    }

    static void SetGeneral(JsonElement msg, string rawId)
    {
        var cfg = AppConfig.Current;
        var start = Bool(msg, "startWithWindows");
        var unfocused = Bool(msg, "onlyWhenUnfocused");
        var port = Int(msg, "port");
        bool restartRequired = false;

        if (start.HasValue && start.Value != cfg.StartWithWindows)
        {
            cfg.StartWithWindows = start.Value;
            WindowsStartup.SetEnabled(start.Value);
        }
        if (unfocused.HasValue) cfg.OnlyWhenUnfocused = unfocused.Value;
        // the port applies at the next start: a live rebind could take down a working server
        if (port is int p && p >= 1024 && p <= 65535 && p != cfg.Port)
        {
            cfg.Port = p;
            restartRequired = true;
        }
        AppConfig.Save();
        PostReply(rawId, new
        {
            port = cfg.Port,
            startWithWindows = cfg.StartWithWindows,
            onlyWhenUnfocused = cfg.OnlyWhenUnfocused,
            restartRequired
        });
    }

    static void SetQuietHours(JsonElement msg, string rawId)
    {
        var cfg = AppConfig.Current;
        var enabled = Bool(msg, "enabled");
        var start = Str(msg, "start");
        var end = Str(msg, "end");
        var allow = Bool(msg, "allowAlarms");
        if (enabled.HasValue) cfg.Quiet.Enabled = enabled.Value;
        if (start != null) cfg.Quiet.Start = start.Trim();
        if (end != null) cfg.Quiet.End = end.Trim();
        if (allow.HasValue) cfg.Quiet.AllowAlarms = allow.Value;
        AppConfig.Save();
        PostReply(rawId, new { cfg.Quiet.Enabled, cfg.Quiet.Start, cfg.Quiet.End, cfg.Quiet.AllowAlarms });
    }

    static void SetUptime(JsonElement msg, string rawId)
    {
        var urls = StrList(msg, "urls");
        if (urls == null)
        {
            PostError(rawId, "setUptime needs a urls array");
            return;
        }
        AppConfig.Current.UptimeUrls = urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        AppConfig.Save(); // UptimeChecker reads the config every tick, so this applies by itself
        PostReply(rawId, new { uptimeUrls = AppConfig.Current.UptimeUrls });
    }

    static void SetCountdown(JsonElement msg, string rawId)
    {
        var cfg = AppConfig.Current;
        var enabled = Bool(msg, "enabled");
        var target = Str(msg, "targetLocal");
        if (enabled.HasValue) cfg.Countdown.Enabled = enabled.Value;
        if (target != null)
        {
            if (!DateTime.TryParse(target, out var when))
            {
                PostError(rawId, "targetLocal is not a date and time");
                return;
            }
            cfg.Countdown.TargetLocal = when;
        }
        AppConfig.Save();
        PostReply(rawId, new { cfg.Countdown.Enabled, targetLocal = cfg.Countdown.TargetLocal });
    }

    static void OpenUrl(JsonElement msg, string rawId)
    {
        var url = Str(msg, "url");
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            PostError(rawId, "only http and https links can be opened");
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
        PostReply(rawId, null);
    }

    // ---------------------------------------------------------------- shared actions
    // The tray menu calls these too, so the page and the tray can never disagree.

    /// <summary>
    /// Shared mute for the page (setMute) and the tray. With minutes it is a timed mute
    /// (MutedUntil); without, Sounds off (Muted) until unmuted. Unmute clears both.
    /// Saves and pushes state so tray icon, tooltip and page header all follow.
    /// </summary>
    public static void SetMuted(bool muted, int? minutes = null)
    {
        var cfg = AppConfig.Current;
        if (!muted)
        {
            cfg.Muted = false;
            cfg.MutedUntil = null;
        }
        else if (minutes is int m && m > 0)
        {
            cfg.Muted = false;
            cfg.MutedUntil = DateTime.Now.AddMinutes(m);
        }
        else
        {
            cfg.Muted = true;
        }
        AppConfig.Save();
        NotifyStateChanged();
    }

    /// <summary>Turns one event on or off and saves. Returns false for an unknown event id.</summary>
    public static bool SetEventEnabled(string eventId, bool enabled)
    {
        var cfg = AppConfig.Current;
        if (!cfg.Events.ContainsKey(eventId)) return false;
        ApplyEventEnabled(cfg, eventId, enabled);
        AppConfig.Save();
        return true;
    }

    /// <summary>
    /// Sets the watcher flag and keeps the "Claude desktop app: finished" event switch
    /// in agreement (one source of truth, both directions), then starts or stops the
    /// live watcher. Saves immediately.
    /// </summary>
    public static void SetWatcherEnabled(bool enabled)
    {
        var cfg = AppConfig.Current;
        cfg.Features.ClaudeDesktopWatcher = enabled;
        if (cfg.Events.TryGetValue(ClaudeDesktopEventId, out var ec))
            ec.Enabled = enabled;
        Services.RunClaudeDesktopWatcher?.Invoke(enabled);
        AppConfig.Save();
    }

    /// <summary>
    /// Applies an enabled change and keeps the Claude desktop pair in lockstep: turning
    /// that event on starts the watcher, turning it off stops it. Returns true when the
    /// watcher flag moved.
    /// </summary>
    static bool ApplyEventEnabled(AppConfig cfg, string eventId, bool enabled)
    {
        if (!cfg.Events.TryGetValue(eventId, out var ec)) return false;
        ec.Enabled = enabled;
        if (eventId != ClaudeDesktopEventId || cfg.Features.ClaudeDesktopWatcher == enabled) return false;
        cfg.Features.ClaudeDesktopWatcher = enabled;
        Services.RunClaudeDesktopWatcher?.Invoke(enabled);
        return true;
    }

    // ---------------------------------------------------------------- state out

    /// <summary>The state object shared by the getState reply and the pushed state message. Never contains the GitHub token.</summary>
    public static object BuildState()
    {
        var cfg = AppConfig.Current;
        return new
        {
            version = VersionText(),
            muted = EventBus.IsMutedNow(cfg),
            mutedUntil = cfg.MutedUntil,
            sounds = EventRegistry.All.Select(d =>
            {
                var ec = cfg.Events.TryGetValue(d.Id, out var e) ? e : new EventConfig();
                return new
                {
                    id = d.Id,
                    name = d.DisplayName,
                    group = d.Category,
                    enabled = ec.Enabled,
                    volume = ec.Volume,
                    soundPath = ec.SoundPath,
                    hasCustomSound = !string.IsNullOrWhiteSpace(ec.SoundPath),
                    defaultSound = d.DefaultSound,
                    focusApps = ec.FocusApps,
                    defaultFocusApps = d.DefaultFocusApps
                };
            }).ToArray(),
            github = GithubState(),
            watcherEnabled = cfg.Features.ClaudeDesktopWatcher,
            quiet = new { enabled = cfg.Quiet.Enabled, start = cfg.Quiet.Start, end = cfg.Quiet.End, allowAlarms = cfg.Quiet.AllowAlarms },
            uptimeUrls = cfg.UptimeUrls,
            countdown = new { enabled = cfg.Countdown.Enabled, targetLocal = cfg.Countdown.TargetLocal },
            general = new
            {
                port = cfg.Port,
                startWithWindows = cfg.StartWithWindows,
                onlyWhenUnfocused = cfg.OnlyWhenUnfocused
            }
        };
    }

    static object GithubState()
    {
        var gh = AppConfig.Current.GitHub;
        // the token itself never leaves this process; the page only learns whether one is set
        return new
        {
            tokenSet = !string.IsNullOrWhiteSpace(gh.Token),
            username = gh.Username,
            pollSeconds = Math.Clamp(gh.PollSeconds, 5, 300),
            repos = gh.Repos
        };
    }

    static void PushState() => PostRaw("{\"type\":\"state\",\"data\":" + JsonSerializer.Serialize(BuildState()) + "}");

    static int HookPort() => Services.ListeningPort?.Invoke() ?? AppConfig.Current.Port;

    static string VersionText()
    {
        var v = typeof(UiBridge).Assembly.GetName().Version;
        return v == null ? "" : (v.Build < 0 ? $"{v.Major}.{v.Minor}" : $"{v.Major}.{v.Minor}.{v.Build}");
    }

    // ---------------------------------------------------------------- plumbing

    static void PostReply(string rawId, object data) =>
        PostRaw("{\"id\":" + rawId + ",\"ok\":true,\"data\":" + JsonSerializer.Serialize(data) + "}");

    static void PostError(string rawId, string error) =>
        PostRaw("{\"id\":" + rawId + ",\"ok\":false,\"error\":" + JsonSerializer.Serialize(error) + "}");

    static void PostRaw(string json)
    {
        try { _post?.Invoke(json); }
        catch (Exception ex) { Logger.Info("bridge: post to page failed: " + ex.Message); }
    }

    static readonly object _saveGate = new();
    static Timer _volumeSaveTimer;

    /// <summary>Coalesces volume-slider saves: one write per 300ms no matter how the drag goes.</summary>
    static void SaveVolumeDebounced()
    {
        lock (_saveGate)
        {
            if (_volumeSaveTimer == null)
                _volumeSaveTimer = new Timer(_ =>
                {
                    lock (_saveGate) AppConfig.Save();
                }, null, Timeout.Infinite, Timeout.Infinite);
            _volumeSaveTimer.Change(300, Timeout.Infinite);
        }
    }

    /// <summary>Writes a pending volume save right away; the app host calls this on exit so no change is lost.</summary>
    public static void FlushPendingSave()
    {
        lock (_saveGate)
            _volumeSaveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        AppConfig.Save();
    }

    // ---- JSON param readers (the page posts flat params next to id and type) ----

    static string Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    static bool? Bool(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var e)) return null;
        return e.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    static int? Int(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.Number) return null;
        try { return e.GetInt32(); }
        catch (FormatException) { return null; }
        catch (OverflowException) { return null; }
    }

    static List<string> StrList(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.Array) return null;
        var list = new List<string>();
        foreach (var v in e.EnumerateArray())
            if (v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                list.Add(v.GetString().Trim());
        return list;
    }
}

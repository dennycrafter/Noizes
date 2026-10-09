using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using Timer = System.Threading.Timer;

namespace Noizes;

/// <summary>
/// Polls the GitHub events API on a configurable interval (default 10 s, clamped 5-300 s)
/// and fires events for pushes, PRs opened/merged and failed checks. Tier 3 adds stars,
/// issues and comments. ETag conditional requests and the rate-limit cooldown keep the
/// cost at one cheap conditional request per interval.
/// </summary>
public class GitHubPoller
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    string _etag;
    readonly HashSet<string> _seen = new();
    bool _primed;
    DateTime _cooldownUntil;
    Timer _timer;

    /// <summary>Interval the timer is currently armed with, in seconds (clamped 5..300).</summary>
    public int PollSeconds { get; private set; }

    public void Start()
    {
        PollSeconds = Math.Clamp(AppConfig.Current.GitHub.PollSeconds, 5, 300);
        _timer = new Timer(_ => _ = PollSafe(), null, TimeSpan.Zero, TimeSpan.FromSeconds(PollSeconds));
    }

    public void Stop() => _timer?.Change(Timeout.Infinite, Timeout.Infinite);

    /// <summary>Re-arms the timer from the current config; used when settings change the interval.</summary>
    public void Restart()
    {
        if (_timer == null) return; // never started
        PollSeconds = Math.Clamp(AppConfig.Current.GitHub.PollSeconds, 5, 300);
        _timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(PollSeconds));
    }

    async Task PollSafe()
    {
        try { await Poll(); }
        catch (Exception ex) { Logger.Info("github poll error: " + ex.Message); }
    }

    async Task Poll()
    {
        var cfg = AppConfig.Current;
        var token = cfg.GitHub.Token;
        var user = cfg.GitHub.Username;
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(user)) return;
        if (DateTime.UtcNow < _cooldownUntil) return;

        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/users/{Uri.EscapeDataString(user)}/events?per_page=30");
        req.Headers.UserAgent.ParseAdd("Noizes/1.0");
        req.Headers.Add("Accept", "application/vnd.github+json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(_etag))
            req.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(_etag));

        using var resp = await Http.SendAsync(req);

        if (resp.Headers.TryGetValues("X-RateLimit-Remaining", out var remVals) &&
            int.TryParse(remVals.FirstOrDefault(), out var remaining) && remaining < 30)
        {
            _cooldownUntil = DateTime.UtcNow.AddMinutes(5);
            Logger.Info("github rate limit low, cooling down 5 minutes");
        }

        if ((int)resp.StatusCode == 304) return;
        if ((int)resp.StatusCode != 200)
        {
            Logger.Info($"github poll status {(int)resp.StatusCode}");
            return;
        }
        if (resp.Headers.ETag != null) _etag = resp.Headers.ETag.ToString();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var fresh = new List<(string id, string type, JsonElement payload, string repo)>();
        foreach (var ev in doc.RootElement.EnumerateArray())
        {
            var id = ev.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (id == null || _seen.Contains(id)) continue;
            _seen.Add(id);

            var type = ev.TryGetProperty("type", out var t) ? (t.GetString() ?? "") : "";
            var repo = ev.TryGetProperty("repo", out var rp) && rp.TryGetProperty("name", out var nm)
                ? (nm.GetString() ?? "") : "";
            var payload = ev.TryGetProperty("payload", out var pl) ? pl.Clone() : default;
            fresh.Add((id, type, payload, repo));
        }

        if (!_primed)
        {
            // first run: just remember what's there so we don't blast old events
            _primed = true;
            return;
        }

        fresh.Reverse(); // API returns newest first; play oldest first
        foreach (var e in fresh) Handle(e.type, e.payload, e.repo);

        if (_seen.Count > 800) { _seen.Clear(); _primed = false; }
    }

    void Handle(string type, JsonElement payload, string repo)
    {
        try
        {
            if (payload.ValueKind != JsonValueKind.Object) return;
            switch (type)
            {
                case "PushEvent":
                    Fire("gh-push", repo);
                    break;

                case "PullRequestEvent":
                {
                    var action = payload.TryGetProperty("action", out var a) ? (a.GetString() ?? "") : "";
                    if (action == "opened")
                        Fire("gh-pr-opened", repo);
                    else if (action == "closed" &&
                             payload.TryGetProperty("pull_request", out var pr) &&
                             pr.TryGetProperty("merged", out var merged) &&
                             merged.ValueKind == JsonValueKind.True)
                        Fire("gh-pr-merged", repo);
                    break;
                }

                case "StatusEvent":
                {
                    var state = payload.TryGetProperty("state", out var s) ? (s.GetString() ?? "") : "";
                    if (state == "failure") Fire("gh-checks-failed", repo);
                    break;
                }

                case "CheckSuiteEvent":
                {
                    var action = payload.TryGetProperty("action", out var a2) ? (a2.GetString() ?? "") : "";
                    if (action == "completed" &&
                        payload.TryGetProperty("check_suite", out var cs) &&
                        cs.TryGetProperty("conclusion", out var c) &&
                        (c.GetString() ?? "") == "failure")
                        Fire("gh-checks-failed", repo);
                    break;
                }

                case "WatchEvent":
                    if ((payload.TryGetProperty("action", out var aw) ? aw.GetString() : "") == "started")
                        Fire("gh-star", repo);
                    break;

                case "IssuesEvent":
                    if ((payload.TryGetProperty("action", out var ai) ? ai.GetString() : "") == "opened")
                        Fire("gh-issue", repo);
                    break;

                case "IssueCommentEvent":
                    if ((payload.TryGetProperty("action", out var ac) ? ac.GetString() : "") == "created")
                        Fire("gh-comment", repo);
                    break;

                case "PullRequestReviewCommentEvent":
                    if ((payload.TryGetProperty("action", out var ar) ? ar.GetString() : "") == "created")
                        Fire("gh-comment", repo);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Info("github event handle error: " + ex.Message);
        }
    }

    void Fire(string id, string repo)
    {
        Logger.Info($"github event: {id} on {repo}");
        EventBus.Dispatch(id);
    }
}

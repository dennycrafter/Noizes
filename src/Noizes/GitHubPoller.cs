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
/// cost at one cheap conditional request per interval per feed.
/// The user feed (the user's own actions across all repos) is polled alongside repo feeds
/// for every configured "owner/repo" — those see all actors, so merges and pushes by
/// GitHub Apps and collaborators fire too. Event ids are deduped across feeds.
/// </summary>
public class GitHubPoller
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Test seam: SelfTest supplies canned responses instead of hitting api.github.com (like Logger.Sink).</summary>
    internal Func<HttpRequestMessage, HttpResponseMessage> TestHttp;
    readonly Dictionary<string, string> _etags = new(); // per feed: "user" or "repo:owner/name"
    readonly HashSet<string> _seen = new();
    readonly HashSet<string> _primedFeeds = new(); // per feed: first sight seeds history instead of firing it
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

    async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req)
        => TestHttp != null ? TestHttp(req) : await Http.SendAsync(req);

    /// <summary>Timer-free entry for tests: runs one full poll cycle against the (test) responder.</summary>
    internal Task PollOnce() => Poll();

    async Task Poll()
    {
        var gh = AppConfig.Current.GitHub;
        if (string.IsNullOrWhiteSpace(gh.Token) || string.IsNullOrWhiteSpace(gh.Username)) return;
        if (DateTime.UtcNow < _cooldownUntil) return;

        foreach (var (key, url) in Feeds(gh))
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("Noizes/1.0");
            req.Headers.Add("Accept", "application/vnd.github+json");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gh.Token);
            if (_etags.TryGetValue(key, out var etag))
                req.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));

            using var resp = await SendAsync(req);

            if (resp.Headers.TryGetValues("X-RateLimit-Remaining", out var remVals) &&
                int.TryParse(remVals.FirstOrDefault(), out var remaining) && remaining < 30)
            {
                _cooldownUntil = DateTime.UtcNow.AddMinutes(5);
                Logger.Info("github rate limit low, cooling down 5 minutes");
                return; // stop the whole cycle, not just this feed
            }

            if ((int)resp.StatusCode == 304) continue;
            if ((int)resp.StatusCode != 200)
            {
                Logger.Info($"github poll status {(int)resp.StatusCode} ({key})");
                continue;
            }
            if (resp.Headers.ETag != null) _etags[key] = resp.Headers.ETag.ToString();

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

            if (_primedFeeds.Add(key))
                continue; // first sight of this feed: just remember what's there so we don't blast old events

            fresh.Reverse(); // API returns newest first; play oldest first
            foreach (var e in fresh) Handle(e.type, e.payload, e.repo);
        }

        if (_seen.Count > 800) { _seen.Clear(); _primedFeeds.Clear(); }
    }

    /// <summary>
    /// One feed per poll cycle: the user's own activity across all repos, plus a repo feed
    /// for each configured "owner/repo" — repo feeds include every actor, so events by
    /// GitHub Apps and collaborators surface. Event ids are shared between feeds, which is
    /// what dedupes an event that appears in both.
    /// </summary>
    internal IEnumerable<(string key, string url)> Feeds(GitHubConfig gh)
    {
        yield return ("user", $"https://api.github.com/users/{Uri.EscapeDataString(gh.Username)}/events?per_page=30");
        foreach (var raw in gh.Repos)
        {
            var repo = NormalizeRepo(raw);
            if (repo == null)
            {
                Logger.Info($"github: ignoring repo entry '{raw}'");
                continue;
            }
            yield return ($"repo:{repo}", $"https://api.github.com/repos/{repo}/events?per_page=30");
        }
    }

    /// <summary>" https://github.com/owner/repo.git " → "owner/repo"; null when the entry is not a repo path.</summary>
    internal static string NormalizeRepo(string entry)
    {
        var s = (entry ?? "").Trim();
        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) s = s["https://".Length..];
        else if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) s = s["http://".Length..];
        if (s.StartsWith("www.github.com/", StringComparison.OrdinalIgnoreCase)) s = s["www.github.com/".Length..];
        else if (s.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase)) s = s["github.com/".Length..];
        while (s.EndsWith("/")) s = s[..^1];
        if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) s = s[..^4];

        var parts = s.Split('/');
        static bool okPart(string p) =>
            p.Length > 0 && p.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '_');
        return parts.Length == 2 && okPart(parts[0]) && okPart(parts[1]) ? s : null;
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

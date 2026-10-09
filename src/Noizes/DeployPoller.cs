using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using Timer = System.Threading.Timer;

namespace Noizes;

/// <summary>
/// Tier 3: watches GitHub deployment statuses on your repos every 3 minutes.
/// Covers Vercel, Netlify and anything else that reports through GitHub deployments.
/// success -> deploy-succeeded, failure/error -> deploy-failed. First pass observes only.
/// </summary>
public class DeployPoller
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    readonly Dictionary<string, long> _lastDeployment = new();
    readonly HashSet<long> _seenStatuses = new();
    bool _primed;
    Timer _timer;

    public void Start() => _timer = new Timer(_ => _ = TickSafe(), null, TimeSpan.Zero, TimeSpan.FromMinutes(3));
    public void Stop() => _timer?.Change(Timeout.Infinite, Timeout.Infinite);

    async Task TickSafe()
    {
        try { await Tick(); }
        catch (Exception ex) { Logger.Info("deploy poll error: " + ex.Message); }
    }

    async Task Tick()
    {
        var cfg = AppConfig.Current;
        if (string.IsNullOrWhiteSpace(cfg.GitHub.Token) || string.IsNullOrWhiteSpace(cfg.GitHub.Username)) return;

        // own repos, most recently pushed first, capped to keep API usage sane
        using var reposResp = await Get("https://api.github.com/user/repos?affiliation=owner&sort=pushed&direction=desc&per_page=10", cfg.GitHub.Token);
        if ((int)reposResp.StatusCode != 200)
        {
            Logger.Info($"deploy poll: repos status {(int)reposResp.StatusCode}");
            return;
        }
        using var reposDoc = JsonDocument.Parse(await reposResp.Content.ReadAsStringAsync());
        var names = new List<string>();
        foreach (var r in reposDoc.RootElement.EnumerateArray())
        {
            var full = r.TryGetProperty("full_name", out var fn) ? fn.GetString() : null;
            if (!string.IsNullOrEmpty(full)) names.Add(full);
        }

        foreach (var repo in names)
        {
            using var depResp = await Get($"https://api.github.com/repos/{repo}/deployments?per_page=3", cfg.GitHub.Token);
            if ((int)depResp.StatusCode != 200) continue;
            using var depDoc = JsonDocument.Parse(await depResp.Content.ReadAsStringAsync());

            foreach (var d in depDoc.RootElement.EnumerateArray())
            {
                var depId = d.TryGetProperty("id", out var di) ? di.GetInt64() : 0;
                if (depId == 0) continue;
                if (_lastDeployment.TryGetValue(repo, out var seen) && depId <= seen) continue;
                _lastDeployment[repo] = depId;

                using var stResp = await Get($"https://api.github.com/repos/{repo}/deployments/{depId}/statuses?per_page=1", cfg.GitHub.Token);
                if ((int)stResp.StatusCode != 200) continue;
                using var stDoc = JsonDocument.Parse(await stResp.Content.ReadAsStringAsync());

                foreach (var st in stDoc.RootElement.EnumerateArray())
                {
                    var state = st.TryGetProperty("state", out var s) ? (s.GetString() ?? "") : "";
                    var stId = st.TryGetProperty("id", out var si) ? si.GetInt64() : 0;
                    if (stId != 0 && !_seenStatuses.Add(stId)) continue;
                    if (!_primed) break; // first pass: observe only

                    if (state == "success")
                    {
                        Logger.Info($"deploy succeeded: {repo}");
                        EventBus.Dispatch("deploy-succeeded");
                    }
                    else if (state == "failure" || state == "error")
                    {
                        Logger.Info($"deploy failed: {repo}");
                        EventBus.Dispatch("deploy-failed");
                    }
                    break; // latest status only; a later success/failure gets a new status id
                }
            }
        }

        _primed = true;
        if (_seenStatuses.Count > 2000) _seenStatuses.Clear();
    }

    static async Task<HttpResponseMessage> Get(string url, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("Noizes/1.0");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Http.SendAsync(req);
    }
}

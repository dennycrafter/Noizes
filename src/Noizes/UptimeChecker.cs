using System.Net.Http;
using System.Threading;
using Timer = System.Threading.Timer;

namespace Noizes;

/// <summary>
/// Tier 4: checks each pasted URL every 3 minutes. When a URL fails twice in a row,
/// plays the uptime alarm once per outage (not on every continued failure).
/// Any HTTP response below 500 counts as "up" (a 404 page still means the site answered).
/// </summary>
public class UptimeChecker
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    readonly Dictionary<string, int> _consecutiveFailures = new();
    readonly HashSet<string> _alarmed = new();
    Timer _timer;

    public void Start() => _timer = new Timer(_ => _ = TickSafe(), null, TimeSpan.Zero, TimeSpan.FromMinutes(3));
    public void Stop() => _timer?.Change(Timeout.Infinite, Timeout.Infinite);

    async Task TickSafe()
    {
        try { await Tick(); }
        catch (Exception ex) { Logger.Info("uptime check error: " + ex.Message); }
    }

    async Task Tick()
    {
        var urls = AppConfig.Current.UptimeUrls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var url in urls)
        {
            bool up;
            try
            {
                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                up = (int)resp.StatusCode < 500;
            }
            catch { up = false; }

            if (up)
            {
                _consecutiveFailures[url] = 0;
                _alarmed.Remove(url);
                continue;
            }

            var fails = (_consecutiveFailures.TryGetValue(url, out var f) ? f : 0) + 1;
            _consecutiveFailures[url] = fails;
            if (fails >= 2 && _alarmed.Add(url))
            {
                Logger.Info($"uptime alarm: {url} failed {fails} checks in a row");
                EventBus.Dispatch("uptime-alarm");
            }
        }
    }
}

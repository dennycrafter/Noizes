using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;
using Timer = System.Threading.Timer;

namespace Noizes;

/// <summary>
/// Tier 2: watches the Claude desktop app via UI Automation. When its "Stop" button
/// (visible while a response is streaming) disappears, the response has finished.
/// Reliability caveat: Electron apps only expose their UIA tree when accessibility is
/// active; if the button is not found this watcher simply stays quiet. See README.
/// </summary>
public class ClaudeDesktopWatcher
{
    Timer _timer;
    bool _stopSeen;
    bool _running;
    DateTime _lastFireUtc = DateTime.MinValue;

    public void Start()
    {
        if (_running) return; // start/stop may be called repeatedly as the feature flag changes
        _running = true;
        _stopSeen = false; // a stale stop-seen from a previous run must not fire a spurious event
        _timer = new Timer(_ => TickSafe(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Logger.Info("claude desktop watcher started");
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        Logger.Info("claude desktop watcher stopped");
    }

    void TickSafe()
    {
        try { Tick(); }
        catch (Exception ex) { Logger.Info("claude desktop watcher error: " + ex.Message); }
    }

    void Tick()
    {
        bool stopNow = StopButtonVisible();
        if (stopNow)
        {
            _stopSeen = true;
            return;
        }
        if (_stopSeen)
        {
            _stopSeen = false;
            if ((DateTime.UtcNow - _lastFireUtc).TotalSeconds > 5)
            {
                _lastFireUtc = DateTime.UtcNow;
                Logger.Info("claude desktop: Stop button disappeared -> claude-desktop-done");
                EventBus.Dispatch("claude-desktop-done");
            }
        }
    }

    bool StopButtonVisible()
    {
        try
        {
            var procs = Process.GetProcessesByName("claude");
            foreach (var p in procs)
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero) continue;
                    var root = AutomationElement.FromHandle(p.MainWindowHandle);
                    if (root == null) continue;

                    // any clickable element named "Stop" (button or generic control)
                    var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                    var found = root.FindAll(TreeScope.Descendants, cond);
                    foreach (AutomationElement el in found)
                    {
                        string name = null;
                        try { name = el.Current.Name; } catch { }
                        if (name != null &&
                            (name.Equals("Stop", StringComparison.OrdinalIgnoreCase) ||
                             name.Equals("Stop response", StringComparison.OrdinalIgnoreCase)))
                            return true;
                    }
                }
                finally { p.Dispose(); }
            }
            return false;
        }
        catch (Exception ex)
        {
            Logger.Info("uia error: " + ex.Message);
            return false;
        }
    }
}

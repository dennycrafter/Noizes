using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;
using Timer = System.Threading.Timer;

namespace Noizes;

/// <summary>
/// Tier 2: watches the Claude desktop app via UI Automation. While a reply is being written
/// the open chat shows a "Stop" (now "Stop response") button; when it disappears, the reply
/// has finished.
///
/// The app only shows the chat that is open, so the watcher also reads which chat that is
/// (the "&lt;chat name&gt;, rename session" button). The Stop button vanishing because the user
/// clicked to another chat is not a finish, and used to ding the moment they switched
/// (v1.3.0-beta.3, confirmed on a real PC). Now a ding needs the same chat still on screen.
/// A chat that finishes while another one is open cannot be seen at all: the app keeps its
/// sidebar marker at "Running" until it is opened again.
///
/// Reliability caveat: Electron apps only expose their UIA tree when accessibility is
/// active; if the button is not found this watcher simply stays quiet. See README.
/// </summary>
public class ClaudeDesktopWatcher
{
    Timer _timer;
    bool _stopSeen;
    string _stopChat;
    bool _running;
    int _busy; // 1 while a tick runs: a slow UIA read must never overlap the next tick
    DateTime _lastFireUtc = DateTime.MinValue;

    const string RenameSuffix = ", rename session";

    public void Start()
    {
        if (_running) return; // start/stop may be called repeatedly as the feature flag changes
        _running = true;
        _stopSeen = false; // a stale stop-seen from a previous run must not fire a spurious event
        _stopChat = null;
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
        if (Interlocked.Exchange(ref _busy, 1) == 1) return; // previous tick still reading
        try { Tick(); }
        catch (Exception ex) { Logger.Info("claude desktop watcher error: " + ex.Message); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    void Tick()
    {
        var seen = ReadWindow(ButtonNames());
        switch (Decide(_stopSeen, _stopChat, seen.StopVisible, seen.Chat))
        {
            case Step.Working:
                _stopSeen = true;
                _stopChat = seen.Chat;
                break;
            case Step.Finished:
                _stopSeen = false;
                _stopChat = null;
                if ((DateTime.UtcNow - _lastFireUtc).TotalSeconds > 5)
                {
                    _lastFireUtc = DateTime.UtcNow;
                    Logger.Info("claude desktop: Stop button disappeared -> claude-desktop-done");
                    EventBus.Dispatch("claude-desktop-done");
                }
                break;
            case Step.SwitchedAway:
                Logger.Info($"claude desktop: switched from \"{_stopChat}\" to \"{seen.Chat}\" while Claude was working, no ding");
                _stopSeen = false;
                _stopChat = null;
                break;
        }
    }

    public enum Step { Idle, Working, Finished, SwitchedAway }

    /// <summary>What one look at the window means. Pure, so the self-test covers every case.
    /// A finish needs the Stop button gone while the same chat is still open; a different chat
    /// on screen means the user switched away, which is not a finish. An app version without
    /// the rename button reports no chat at all (null), and then the old Stop-only rule applies.</summary>
    public static Step Decide(bool stopSeen, string stopChat, bool stopNow, string chatNow)
    {
        if (stopNow) return Step.Working;
        if (!stopSeen) return Step.Idle;
        return string.Equals(stopChat, chatNow, StringComparison.Ordinal) ? Step.Finished : Step.SwitchedAway;
    }

    public readonly record struct WindowRead(bool StopVisible, string Chat);

    /// <summary>Reads the Stop button and the open chat's name from the window's button names.
    /// Pure, so the self-test can feed it the real names captured from the app.</summary>
    public static WindowRead ReadWindow(IEnumerable<string> buttonNames)
    {
        bool stop = false;
        string chat = null;
        foreach (var name in buttonNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (name.Equals("Stop", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Stop response", StringComparison.OrdinalIgnoreCase))
                stop = true;
            else if (chat == null && name.EndsWith(RenameSuffix, StringComparison.OrdinalIgnoreCase))
                chat = name.Substring(0, name.Length - RenameSuffix.Length);
        }
        return new WindowRead(stop, chat);
    }

    List<string> ButtonNames()
    {
        var names = new List<string>();
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

                    var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                    var found = root.FindAll(TreeScope.Descendants, cond);
                    foreach (AutomationElement el in found)
                    {
                        try { names.Add(el.Current.Name); } catch { }
                    }
                }
                finally { p.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            Logger.Info("uia error: " + ex.Message);
        }
        return names;
    }
}

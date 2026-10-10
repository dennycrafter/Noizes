using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace Noizes;

/// <summary>Owns the tray icon, the local server and the background pollers.</summary>
public class TrayApplicationContext : ApplicationContext
{
    readonly NotifyIcon _tray;
    readonly HttpServer _server = new();
    readonly GitHubPoller _gh = new();
    readonly ClaudeDesktopWatcher _watcher = new();
    readonly DeployPoller _deploy = new();
    readonly UptimeChecker _uptime = new();
    readonly CountdownChecker _countdown = new();
    readonly ToolStripMenuItem _miMute;
    readonly ToolStripMenuItem _miMuteHour;
    readonly Timer _muteClock; // keeps icon, tooltip and check state in step with config changes made elsewhere

    readonly Icon _normalIcon;
    Icon _mutedIcon;
    IntPtr _mutedIconHandle = IntPtr.Zero;

    const string TooltipNormal = "Noizes - done sounds";
    const string TooltipMuted = "Noizes (muted)";

    public TrayApplicationContext(bool startWithUi)
    {
        var menu = new ContextMenuStrip();

        _miMute = new ToolStripMenuItem("Mute sounds");
        _miMute.Click += (s, e) => SetMuted(!IsMutedNow());

        _miMuteHour = new ToolStripMenuItem("Mute for 1 hour");
        _miMuteHour.Click += (s, e) => MuteForOneHour();

        var miSettings = new ToolStripMenuItem("Settings...");
        miSettings.Click += (s, e) => SettingsWindow.Show();

        var miStartup = new ToolStripMenuItem("Start with Windows")
        {
            Checked = AppConfig.Current.StartWithWindows,
            CheckOnClick = true
        };
        miStartup.Click += (s, e) =>
        {
            AppConfig.Current.StartWithWindows = miStartup.Checked;
            WindowsStartup.SetEnabled(miStartup.Checked);
            AppConfig.Save();
        };

        var miExit = new ToolStripMenuItem("Exit");
        miExit.Click += (s, e) => ExitApp();

        menu.Items.AddRange(new ToolStripItem[]
        {
            _miMute, _miMuteHour, new ToolStripSeparator(),
            miSettings, miStartup, new ToolStripSeparator(), miExit
        });
        menu.Opening += (s, e) => RefreshMuteUi();

        Icon icon;
        try { icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { icon = SystemIcons.Application; }
        _normalIcon = icon;

        _tray = new NotifyIcon
        {
            Icon = _normalIcon,
            Text = TooltipNormal,
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (s, e) => SettingsWindow.Show();

        _muteClock = new Timer { Interval = 15000 };
        _muteClock.Tick += (s, e) => OnMuteClockTick();
        _muteClock.Start();

        _server.Start(AppConfig.Current.Port);
        if (!_server.IsListening)
            _tray.ShowBalloonTip(10000, "Noizes",
                $"Port {AppConfig.Current.Port} is busy, so integrations can't reach Noizes. " +
                "Another program may be using it - change the port in Settings.", ToolTipIcon.Warning);
        _gh.Start();
        if (AppConfig.Current.Features.ClaudeDesktopWatcher) _watcher.Start(); // opt-in
        _deploy.Start();
        _uptime.Start();
        _countdown.Start();

        RefreshMuteUi();

        if (startWithUi) SettingsWindow.Show();
    }

    /// <summary>True while either mute source is active: the indefinite switch or a timed mute.</summary>
    bool IsMutedNow()
    {
        var until = AppConfig.Current.MutedUntil;
        return AppConfig.Current.Muted || (until.HasValue && until.Value > DateTime.Now);
    }

    void SetMuted(bool muted)
    {
        // Same config layer the settings page writes through; the bridge pushes state after changes.
        AppConfig.Current.Muted = muted;
        if (!muted) AppConfig.Current.MutedUntil = null; // unmuting also cancels a running timed mute
        AppConfig.Save();
        RefreshMuteUi();
    }

    void MuteForOneHour()
    {
        // Timed mute only: Muted stays false so sounds come back on their own when MutedUntil passes.
        AppConfig.Current.MutedUntil = DateTime.Now.AddHours(1);
        AppConfig.Save();
        RefreshMuteUi();
    }

    void OnMuteClockTick()
    {
        if (AppConfig.Current.MutedUntil is { } until && until <= DateTime.Now)
        {
            // Dispatch clears it too, but the tray should not wait for an event to notice.
            AppConfig.Current.MutedUntil = null;
            AppConfig.Save();
        }
        RefreshMuteUi();
    }

    void RefreshMuteUi()
    {
        bool muted = IsMutedNow();
        _miMute.Checked = muted;
        _tray.Text = muted ? TooltipMuted : TooltipNormal;
        _tray.Icon = muted ? GetMutedIcon() : _normalIcon;
    }

    /// <summary>The normal app icon with a slash drawn over it, built once and reused.</summary>
    Icon GetMutedIcon()
    {
        if (_mutedIcon != null) return _mutedIcon;

        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawIcon(_normalIcon, new Rectangle(0, 0, size, size));
            using var outline = new Pen(Color.White, 6f);
            using var slash = new Pen(Color.FromArgb(255, 111, 102), 3f); // UI palette --bad
            g.DrawLine(outline, 7, 7, size - 7, size - 7);
            g.DrawLine(slash, 7, 7, size - 7, size - 7);
        }

        _mutedIconHandle = bmp.GetHicon();
        _mutedIcon = Icon.FromHandle(_mutedIconHandle);
        return _mutedIcon;
    }

    void ExitApp()
    {
        try
        {
            _muteClock.Stop();
            _muteClock.Dispose();
            if (_mutedIcon != null)
            {
                _tray.Icon = _normalIcon;
                _mutedIcon.Dispose();
                DestroyIcon(_mutedIconHandle);
                _mutedIcon = null;
                _mutedIconHandle = IntPtr.Zero;
            }
            _tray.Visible = false;
            _tray.Dispose();
            _server.Stop();
            _gh.Stop();
            _watcher.Stop();
            _deploy.Stop();
            _uptime.Stop();
            _countdown.Stop();
        }
        catch { }
        Application.Exit();
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);
}

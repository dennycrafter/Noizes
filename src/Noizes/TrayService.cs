using System.Drawing;
using System.Windows.Forms;

namespace Noizes;

/// <summary>Owns the tray icon, the local server and the background pollers.</summary>
public class TrayApplicationContext : ApplicationContext
{
    readonly NotifyIcon _tray;
    SettingsForm _form;
    readonly HttpServer _server = new();
    readonly GitHubPoller _gh = new();
    readonly ClaudeDesktopWatcher _watcher = new();
    readonly DeployPoller _deploy = new();

    public TrayApplicationContext(bool startWithUi)
    {
        var menu = new ContextMenuStrip();

        var miSettings = new ToolStripMenuItem("Settings...");
        miSettings.Click += (s, e) => ShowSettings();

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

        menu.Items.AddRange(new ToolStripItem[] { miSettings, miStartup, new ToolStripSeparator(), miExit });

        Icon icon;
        try { icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { icon = SystemIcons.Application; }

        _tray = new NotifyIcon
        {
            Icon = icon,
            Text = "Noizes - done sounds",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (s, e) => ShowSettings();

        _server.Start(AppConfig.Current.Port);
        _gh.Start();
        _watcher.Start();
        _deploy.Start();

        if (startWithUi) ShowSettings();
    }

    void ShowSettings()
    {
        if (_form != null && !_form.IsDisposed)
        {
            _form.WindowState = FormWindowState.Normal;
            _form.Activate();
            return;
        }
        _form = new SettingsForm { Server = _server };
        _form.Show();
    }

    void ExitApp()
    {
        try
        {
            _tray.Visible = false;
            _tray.Dispose();
            _server.Stop();
            _gh.Stop();
            _watcher.Stop();
            _deploy.Stop();
        }
        catch { }
        Application.Exit();
    }
}

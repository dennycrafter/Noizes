using System.Drawing;
using System.Windows.Forms;

namespace Noizes;

public class SettingsForm : Form
{
    public HttpServer Server;
    public GitHubPoller GitHub;
    /// <summary>First launch with no config yet: open on the first tab (Integrations) with a one-line pointer.</summary>
    public bool FirstRun;

    public ClaudeDesktopWatcher Watcher; // live start/stop when the feature flag toggles

    // events tab
    readonly ListView _list = new();
    readonly CheckBox _chkEnabled = new() { Text = "Play this event", AutoSize = true };
    readonly TrackBar _vol = new() { TickStyle = TickStyle.None, Minimum = 0, Maximum = 100, Width = 180 };
    readonly Label _lblVol = new() { Width = 44, TextAlign = ContentAlignment.MiddleLeft }; // text always comes from the event's real volume
    readonly TextBox _txtSound = new() { ReadOnly = true, Width = 280 };
    readonly TextBox _txtFocusApps = new() { Width = 300 };
    readonly Button _btnBrowse = new() { Text = "Choose sound…" };
    readonly Button _btnDefault = new() { Text = "Use default" };
    readonly Button _btnTest = new() { Text = "Test" };
    string _currentEvent = "";

    // general tab
    readonly CheckBox _chkStartup = new() { Text = "Start Noizes when Windows starts", AutoSize = true };
    readonly CheckBox _chkUnfocused = new() { Text = "Only play when the source app is not focused", AutoSize = true };
    readonly CheckBox _chkQuiet = new() { Text = "Enable quiet hours (no sounds between these times)", AutoSize = true };
    readonly CheckBox _chkAllowAlarms = new() { Text = "Still play the uptime alarm during quiet hours", AutoSize = true };
    readonly NumericUpDown _numPort = new() { Minimum = 1024, Maximum = 65535, Value = 7351, Width = 90 };
    readonly TextBox _txtQuietStart = new() { Width = 60, MaxLength = 5 };
    readonly TextBox _txtQuietEnd = new() { Width = 60, MaxLength = 5 };

    // integrations tab (first tab — one obvious surface to turn things on and off)
    readonly TabControl _tabs;
    TabPage _integrationsPage, _extrasPage;
    readonly Dictionary<string, Label> _integStatus = new();
    readonly Button _btnClaude = new() { Text = "Set up" };
    readonly Button _btnCursor = new() { Text = "Set up" };
    readonly Button _btnGhReveal = new() { Text = "Add token…" };
    readonly Button _btnWatcher = new() { Text = "Turn on" };
    readonly Button _btnChromeHow = new() { Text = "How do I add it?" };
    readonly Button _btnExtrasGo = new() { Text = "Configure…" };
    GroupBox _ghPanel;
    Font _boldFont;
    readonly TextBox _txtToken = new() { Width = 340, UseSystemPasswordChar = true };
    readonly TextBox _txtUser = new() { Width = 180 };
    readonly NumericUpDown _numPoll = new() { Minimum = 5, Maximum = 300, Value = 10, Width = 90 };
    readonly Button _btnSaveGh = new() { Text = "Save" };
    readonly Button _btnTestGh = new() { Text = "Test" };
    readonly Label _lblGhStatus = new() { Text = "", AutoSize = true };

    // extras tab
    readonly TextBox _txtUrls = new()
    {
        Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 120,
        Dock = DockStyle.Fill, AcceptsReturn = true, Font = new Font("Consolas", 9f)
    };
    readonly CheckBox _chkCountdown = new()
    { Text = "Enable countdown (plays at 1 hour, 30 minutes and 10 minutes before)", AutoSize = true };
    readonly DateTimePicker _dtp = new()
    { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd  HH:mm", ShowUpDown = true, Width = 170 };

    public SettingsForm()
    {
        Text = "Noizes settings";
        Size = new Size(780, 660);
        MinimumSize = new Size(720, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        // real icon in the title bar - same extraction the tray uses (csproj embeds the app icon)
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { Icon = SystemIcons.Application; }

        var tabs = _tabs = new TabControl { Dock = DockStyle.Fill, TabIndex = 0 };
        _boldFont = new Font(Font, FontStyle.Bold);
        Disposed += (s, e) => _boldFont.Dispose();

        tabs.TabPages.Add(BuildIntegrationsTab());
        tabs.TabPages.Add(BuildEventsTab());
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildExtrasTab());
        _tabs.SelectedIndexChanged += (s, e) =>
        {
            if (_tabs.SelectedTab == _integrationsPage) RefreshIntegrationStatuses(); // keep status text live
        };

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, TabIndex = 1,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8)
        };
        var btnClose = new Button { Text = "Close", TabIndex = 1 };
        btnClose.Click += (s, e) => Close();
        var btnSave = new Button { Text = "Save settings", TabIndex = 0 };
        btnSave.Click += (s, e) => SaveAll();
        bottom.Controls.Add(btnClose);
        bottom.Controls.Add(btnSave);

        Controls.Add(_tabs);
        Controls.Add(bottom);

        AcceptButton = btnSave; // Enter saves
        CancelButton = btnClose; // Esc closes

        LoadAll();
    }

    TabPage BuildEventsTab()
    {
        var page = new TabPage("Events");

        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.Columns.Add("Event", 300);
        _list.Columns.Add("On/Off", 60);
        _list.Columns.Add("Volume", 60);
        _list.Columns.Add("Sound", 240);
        _list.Dock = DockStyle.Fill;
        _list.TabIndex = 0;
        _list.ShowGroups = true;
        var groups = new Dictionary<string, ListViewGroup>();
        foreach (var def in EventRegistry.All)
        {
            // first row in a category creates its group; groups render in registry order
            if (!groups.TryGetValue(def.Category, out var group))
            {
                group = new ListViewGroup(def.Category);
                groups[def.Category] = group;
                _list.Groups.Add(group);
            }
            var item = new ListViewItem(new[] { def.DisplayName, "", "", "" }) { Tag = def.Id, Group = group };
            _list.Items.Add(item);
            StyleEventRow(item);
        }
        _list.SelectedIndexChanged += (s, e) => LoadSelectedEvent();

        var detail = new GroupBox { Text = "Selected event", Dock = DockStyle.Bottom, AutoSize = true, TabIndex = 1, Padding = new Padding(10) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        grid.Controls.Add(new Label { Text = "Enabled:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) }, 0, 0);
        _chkEnabled.TabIndex = 0;
        grid.Controls.Add(_chkEnabled, 1, 0);
        grid.Controls.Add(new Label { Text = "Volume:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) }, 0, 1);
        var volPanel = new FlowLayoutPanel { AutoSize = true, Width = 320, TabIndex = 1 };
        _vol.TabIndex = 0;
        volPanel.Controls.Add(_vol);
        volPanel.Controls.Add(_lblVol);
        _vol.Scroll += (s, e) => RefreshVolumeLabel();
        grid.Controls.Add(volPanel, 1, 1);
        grid.Controls.Add(new Label { Text = "Sound:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) }, 0, 2);
        var soundPanel = new FlowLayoutPanel { AutoSize = true, Width = 600, TabIndex = 2 };
        _txtSound.TabIndex = 0;
        _btnBrowse.TabIndex = 1;
        _btnDefault.TabIndex = 2;
        _btnTest.TabIndex = 3;
        soundPanel.Controls.Add(_txtSound);
        soundPanel.Controls.Add(_btnBrowse);
        soundPanel.Controls.Add(_btnDefault);
        soundPanel.Controls.Add(_btnTest);
        grid.Controls.Add(soundPanel, 1, 2);
        var focusHint = new Label
        {
            Text = "Only play when none of these apps is focused (comma-separated process names, blank = smart default):",
            AutoSize = true, Margin = new Padding(3, 8, 3, 0)
        };
        grid.Controls.Add(focusHint, 0, 3);
        grid.SetColumnSpan(focusHint, 2);
        _txtFocusApps.TabIndex = 3;
        grid.Controls.Add(_txtFocusApps, 1, 4);
        detail.Controls.Add(grid);

        _btnBrowse.Click += (s, e) => BrowseSound();
        _btnDefault.Click += (s, e) => { _txtSound.Text = ""; ApplySelectedEvent(); };
        _btnTest.Click += (s, e) => TestSelected();

        page.Controls.Add(_list);
        page.Controls.Add(detail);
        return page;
    }

    TabPage BuildGeneralTab()
    {
        var page = new TabPage("General");
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            AutoSize = true, Padding = new Padding(12), WrapContents = false
        };

        _chkStartup.TabIndex = 0;
        panel.Controls.Add(_chkStartup);
        _chkUnfocused.TabIndex = 1;
        panel.Controls.Add(_chkUnfocused);

        var portPanel = new FlowLayoutPanel { AutoSize = true, Width = 640, TabIndex = 2 };
        portPanel.Controls.Add(new Label { Text = "Local server port:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        _numPort.TabIndex = 0;
        portPanel.Controls.Add(_numPort);
        portPanel.Controls.Add(new Label { Text = "(hooks call http://127.0.0.1:<port>/event/...)", AutoSize = true, Margin = new Padding(12, 8, 3, 0) });
        panel.Controls.Add(portPanel);

        // a real GroupBox like the Extras tab, so quiet hours reads as one grouped setting
        var quietGroup = new GroupBox { Text = "Quiet hours", AutoSize = true, Width = 700, TabIndex = 3, Padding = new Padding(10) };
        var quietPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Width = 660, WrapContents = false };
        _chkQuiet.TabIndex = 0;
        quietPanel.Controls.Add(_chkQuiet);
        var quietTimes = new FlowLayoutPanel { AutoSize = true, Width = 640, TabIndex = 1 };
        quietTimes.Controls.Add(new Label { Text = "from", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        _txtQuietStart.TabIndex = 0;
        quietTimes.Controls.Add(_txtQuietStart);
        quietTimes.Controls.Add(new Label { Text = "to", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        _txtQuietEnd.TabIndex = 1;
        quietTimes.Controls.Add(_txtQuietEnd);
        quietTimes.Controls.Add(new Label { Text = "(24-hour HH:MM, e.g. 22:00 and 07:00)", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        quietPanel.Controls.Add(quietTimes);
        quietGroup.Controls.Add(quietPanel);
        panel.Controls.Add(quietGroup);
        _chkAllowAlarms.TabIndex = 4;
        panel.Controls.Add(_chkAllowAlarms);

        page.Controls.Add(panel);
        return page;
    }

    TabPage BuildIntegrationsTab()
    {
        var page = new TabPage("Integrations");
        _integrationsPage = page;

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            AutoScroll = true, Padding = new Padding(12), WrapContents = false, TabIndex = 0
        };

        panel.Controls.Add(IntegrationRow("Claude Code",
            "Adds the \"finished\" and \"needs your input\" hooks to ~/.claude/settings.json. Your file is backed up first and nothing already in it is removed. Open a NEW Claude Code session afterwards.",
            "claude", _btnClaude, 0));
        panel.Controls.Add(IntegrationRow("Cursor",
            "Adds an agent \"stop\" hook to ~/.cursor/hooks.json. Your file is backed up first and nothing already in it is removed. Restart Cursor afterwards.",
            "cursor", _btnCursor, 1));
        panel.Controls.Add(IntegrationRow("GitHub activity",
            "Sounds for pushes, PRs, failed checks, stars, issues and comments on your account. Your token stays on this computer and is only sent to api.github.com.",
            "github", _btnGhReveal, 2));
        panel.Controls.Add(IntegrationRow("Claude desktop app",
            "Watches the desktop app's Stop button and plays a sound when a response finishes. Off by default - nothing is watched until you turn it on.",
            "watcher", _btnWatcher, 3));
        panel.Controls.Add(IntegrationRow("Chrome extension",
            "Sounds for finished claude.ai and Obvious chats, watched tabs and downloads in Chrome. Loaded from chrome://extensions - no store needed.",
            "chrome", _btnChromeHow, 4));
        panel.Controls.Add(IntegrationRow("Uptime watch, countdown & long commands",
            "Uptime checks and the countdown are configured on the Uptime & countdown tab. Long commands play a sound when you run them through 'noizes run' in a terminal.",
            "extras", _btnExtrasGo, 5));

        _btnClaude.Click += (s, e) => { ConnectClaude(); RefreshIntegrationStatuses(); };
        _btnCursor.Click += (s, e) => { ConnectCursor(); RefreshIntegrationStatuses(); };
        _btnGhReveal.Click += (s, e) => ToggleGitHubPanel();
        _btnWatcher.Click += (s, e) => ToggleWatcher();
        _btnChromeHow.Click += (s, e) => ShowChromeHowTo();
        _btnExtrasGo.Click += (s, e) => _tabs.SelectedTab = _extrasPage;
        _btnSaveGh.Click += (s, e) =>
        {
            ApplyGitHub();
            AppConfig.Save();
            _lblGhStatus.Text = "Saved.";
            RefreshIntegrationStatuses();
        };
        _btnTestGh.Click += (s, e) => _ = TestGitHub();

        page.Controls.Add(panel);
        page.Controls.Add(BuildGitHubPanel());
        _ghPanel.TabIndex = 1; // after the rows panel
        RefreshIntegrationStatuses();
        return page;
    }

    Control IntegrationRow(string title, string description, string statusKey, Button action, int tabIndex)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true, Width = 712, WrapContents = false, Margin = new Padding(0, 4, 0, 8), TabIndex = tabIndex
        };

        var nameCol = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoSize = true, Width = 424, WrapContents = false,
            Margin = new Padding(0)
        };
        nameCol.Controls.Add(new Label
        {
            Text = title, AutoSize = true, Font = _boldFont, Margin = new Padding(3, 3, 3, 0)
        });
        nameCol.Controls.Add(new Label
        {
            Text = description, AutoSize = true, MaximumSize = new Size(414, 0),
            ForeColor = Color.FromArgb(96, 96, 96), Margin = new Padding(3, 0, 3, 0)
        });
        row.Controls.Add(nameCol);

        var status = new Label
        {
            AutoSize = false, Width = 132, TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(60, 60, 60), Margin = new Padding(8, 6, 8, 0)
        };
        _integStatus[statusKey] = status;
        row.Controls.Add(status);
        action.Margin = new Padding(8, 3, 3, 3);
        action.TabIndex = 0; // the row's single focusable control
        row.Controls.Add(action);
        return row;
    }

    Control BuildGitHubPanel()
    {
        _ghPanel = new GroupBox
        {
            Text = "GitHub settings", Dock = DockStyle.Bottom, Visible = false,
            AutoSize = true, Padding = new Padding(10)
        };
        var p = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false
        };

        p.Controls.Add(new Label
        {
            Text = "Create a read-only token: github.com/settings/personal-access-tokens → Generate new token → fine-grained → Public repositories (read-only), no extra permissions.",
            AutoSize = true, MaximumSize = new Size(650, 0), ForeColor = Color.FromArgb(96, 96, 96)
        });

        var tokRow = new FlowLayoutPanel { AutoSize = true, Width = 660, TabIndex = 0 };
        tokRow.Controls.Add(new Label { Text = "Token:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        _txtToken.TabIndex = 0;
        tokRow.Controls.Add(_txtToken);
        p.Controls.Add(tokRow);

        var userRow = new FlowLayoutPanel { AutoSize = true, Width = 660, TabIndex = 1 };
        userRow.Controls.Add(new Label { Text = "Username:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        _txtUser.TabIndex = 0;
        userRow.Controls.Add(_txtUser);
        p.Controls.Add(userRow);

        var pollRow = new FlowLayoutPanel { AutoSize = true, Width = 660, TabIndex = 2 };
        pollRow.Controls.Add(new Label { Text = "Check every:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        _numPoll.TabIndex = 0;
        pollRow.Controls.Add(_numPoll);
        pollRow.Controls.Add(new Label
        {
            Text = "seconds (5-300). Each check is lightweight: if nothing new, GitHub answers with an empty 304.",
            AutoSize = true, Margin = new Padding(3, 8, 3, 0)
        });
        p.Controls.Add(pollRow);

        var btnRow = new FlowLayoutPanel { AutoSize = true, Width = 660, TabIndex = 3 };
        _btnSaveGh.TabIndex = 0;
        btnRow.Controls.Add(_btnSaveGh);
        _btnTestGh.TabIndex = 1;
        btnRow.Controls.Add(_btnTestGh);
        btnRow.Controls.Add(_lblGhStatus);
        p.Controls.Add(btnRow);

        _ghPanel.Controls.Add(p);
        return _ghPanel;
    }

    void ToggleGitHubPanel()
    {
        _ghPanel.Visible = !_ghPanel.Visible;
        _btnGhReveal.Text = _ghPanel.Visible ? "Hide" : HasGitHubToken() ? "Edit…" : "Add token…";
    }

    void ToggleWatcher()
    {
        AppConfig.Current.Features.ClaudeDesktopWatcher = !AppConfig.Current.Features.ClaudeDesktopWatcher;
        ApplyWatcherFlag();
        AppConfig.Save();
        RefreshIntegrationStatuses();
    }

    void ApplyWatcherFlag()
    {
        if (Watcher == null) return;
        if (AppConfig.Current.Features.ClaudeDesktopWatcher) Watcher.Start();
        else Watcher.Stop();
    }

    void ShowChromeHowTo()
    {
        MessageBox.Show(this,
            "1. Open chrome://extensions in Chrome.\n" +
            "2. Turn on Developer mode (top right), then click Load unpacked.\n" +
            "3. Select the extension folder from the Noizes repository (see README - Chrome extension).\n\n" +
            "After that, browser events are just events: turn them on or off on the Events tab.",
            "Add the Chrome extension", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    void RefreshIntegrationStatuses()
    {
        var c = AppConfig.Current;
        _integStatus["claude"].Text = HookInstalled(ClaudeCodeConnector.SettingsPath, "event/claude-code-done")
            ? "Connected" : "Not connected";
        _integStatus["cursor"].Text = HookInstalled(CursorConnector.HooksPath, "event/cursor-done")
            ? "Connected" : "Not connected";
        _integStatus["github"].Text = HasGitHubToken()
            ? "Watching every " + Math.Clamp(c.GitHub.PollSeconds, 5, 300) + " s" : "Off";
        _integStatus["watcher"].Text = c.Features.ClaudeDesktopWatcher ? "Watching" : "Off";
        _integStatus["chrome"].Text = "-";
        _integStatus["extras"].Text = ExtrasStatus(c);
        _btnWatcher.Text = c.Features.ClaudeDesktopWatcher ? "Turn off" : "Turn on";
        if (!_ghPanel.Visible)
            _btnGhReveal.Text = HasGitHubToken() ? "Edit…" : "Add token…";
    }

    static bool HookInstalled(string file, string marker) =>
        File.Exists(file) && File.ReadAllText(file).Contains(marker, StringComparison.Ordinal);

    static bool HasGitHubToken() => !string.IsNullOrWhiteSpace(AppConfig.Current.GitHub.Token);

    static string ExtrasStatus(AppConfig c)
    {
        var on = (c.UptimeUrls.Count > 0 ? 1 : 0)
                 + (c.Countdown.Enabled ? 1 : 0)
                 + (c.Events.TryGetValue("long-command-done", out var lc) && lc.Enabled ? 1 : 0);
        return on == 0 ? "Off" : $"On ({on} of 3)";
    }

    TabPage BuildExtrasTab()
    {
        var page = new TabPage("Uptime & countdown");
        _extrasPage = page;
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            AutoSize = true, Padding = new Padding(12), WrapContents = false
        };

        var g1 = new GroupBox { Text = "Uptime watch", AutoSize = true, Width = 700, TabIndex = 0, Padding = new Padding(10) };
        var g1p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Width = 660, WrapContents = false, TabIndex = 0 };
        g1p.Controls.Add(new Label
        {
            Text = "One URL per line. Each URL is checked every 3 minutes; if one fails twice in a row, the\n\"Uptime: a site is down\" alarm plays (even during quiet hours unless you say otherwise).",
            AutoSize = true, Width = 650
        });
        _txtUrls.Width = 650;
        _txtUrls.TabIndex = 0;
        g1p.Controls.Add(_txtUrls);
        g1.Controls.Add(g1p);

        var g2 = new GroupBox { Text = "Countdown", AutoSize = true, Width = 700, TabIndex = 1, Padding = new Padding(10) };
        var g2p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Width = 660, WrapContents = false };
        _chkCountdown.TabIndex = 0;
        g2p.Controls.Add(_chkCountdown);
        var dtpRow = new FlowLayoutPanel { AutoSize = true, Width = 650, TabIndex = 1 };
        dtpRow.Controls.Add(new Label { Text = "Target date & time:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        _dtp.TabIndex = 0;
        dtpRow.Controls.Add(_dtp);
        g2p.Controls.Add(dtpRow);
        g2.Controls.Add(g2p);

        panel.Controls.Add(g1);
        panel.Controls.Add(g2);
        page.Controls.Add(panel);
        return page;
    }

    void LoadSelectedEvent()
    {
        if (_list.SelectedItems.Count == 0) return;
        var item = _list.SelectedItems[0];
        _currentEvent = (string)item.Tag;
        var ec = AppConfig.Current.Events[_currentEvent];
        _chkEnabled.Checked = ec.Enabled;
        _vol.Value = Math.Clamp(ec.Volume, 0, 100);
        RefreshVolumeLabel();
        _txtSound.Text = ec.SoundPath ?? "";
        _txtFocusApps.Text = string.Join(", ", ec.FocusApps);
    }

    void ApplySelectedEvent()
    {
        if (string.IsNullOrEmpty(_currentEvent)) return;
        var ec = AppConfig.Current.Events[_currentEvent];
        ec.Enabled = _chkEnabled.Checked;
        ec.Volume = _vol.Value;
        ec.SoundPath = _txtSound.Text.Trim();
        ec.FocusApps = _txtFocusApps.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        RefreshList();
    }

    void RefreshList()
    {
        foreach (ListViewItem item in _list.Items)
            StyleEventRow(item);
    }

    /// <summary>One place for the row's text and state styling so On/Off can't be missed.</summary>
    void StyleEventRow(ListViewItem item)
    {
        var ec = AppConfig.Current.Events[(string)item.Tag];
        item.UseItemStyleForSubItems = false;
        item.SubItems[1].Text = ec.Enabled ? "On" : "Off";
        item.SubItems[1].ForeColor = ec.Enabled ? Color.FromArgb(0, 128, 0) : Color.Gray;
        item.SubItems[1].Font = ec.Enabled ? _boldFont : _list.Font; // reset font so a row turned Off isn't left bold
        item.SubItems[2].Text = ec.Volume + "%";
        item.SubItems[3].Text = string.IsNullOrEmpty(ec.SoundPath) ? "(default)" : Path.GetFileName(ec.SoundPath);
    }

    void RefreshVolumeLabel() => _lblVol.Text = _vol.Value + "%";

    void BrowseSound()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose a sound for this event",
            Filter = "Sounds (*.mp3;*.wav)|*.mp3;*.wav|All files|*.*"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _txtSound.Text = dlg.FileName;
            ApplySelectedEvent();
        }
    }

    void TestSelected()
    {
        ApplySelectedEvent();
        if (string.IsNullOrEmpty(_currentEvent)) return;
        var def = EventRegistry.Get(_currentEvent);
        var ec = AppConfig.Current.Events[_currentEvent];
        var path = ec.SoundPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            path = Path.Combine(AppConfig.SoundsDir, def.DefaultSound);
        var played = AudioPlayer.Play(_currentEvent + "-test", path, ec.Volume).Played;
        if (!played)
            MessageBox.Show(this, "Could not play that file. Pick an .mp3 or .wav file.", "Noizes",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    void ConnectClaude()
    {
        AppConfig.Current.Port = (int)_numPort.Value;
        var r = ClaudeCodeConnector.Connect(null, AppConfig.Current.Port);
        MessageBox.Show(this, r.message, r.ok ? "Noizes" : "Noizes - could not connect",
            MessageBoxButtons.OK, r.ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    void ConnectCursor()
    {
        AppConfig.Current.Port = (int)_numPort.Value;
        var r = CursorConnector.Connect(null, AppConfig.Current.Port);
        MessageBox.Show(this, r.message, r.ok ? "Noizes" : "Noizes - could not connect",
            MessageBoxButtons.OK, r.ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    void ApplyGitHub()
    {
        AppConfig.Current.GitHub.Token = _txtToken.Text.Trim();
        AppConfig.Current.GitHub.Username = _txtUser.Text.Trim();
        AppConfig.Current.GitHub.PollSeconds = (int)_numPoll.Value;
    }

    async System.Threading.Tasks.Task TestGitHub()
    {
        ApplyGitHub();
        _btnTestGh.Enabled = false;
        _lblGhStatus.Text = "Testing…";
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            req.Headers.UserAgent.ParseAdd("Noizes/1.0");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AppConfig.Current.GitHub.Token);
            using var resp = await http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if ((int)resp.StatusCode == 200)
            {
                string login = "?";
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    login = doc.RootElement.GetProperty("login").GetString() ?? "?";
                }
                catch { }
                _lblGhStatus.Text = $"Works (signed in as {login}).";
            }
            else
            {
                _lblGhStatus.Text = $"Failed ({(int)resp.StatusCode}). Check the token.";
            }
        }
        catch (Exception ex)
        {
            _lblGhStatus.Text = "Failed: " + ex.Message;
        }
        _btnTestGh.Enabled = true;
    }

    void LoadAll()
    {
        var c = AppConfig.Current;
        _chkStartup.Checked = c.StartWithWindows;
        _chkUnfocused.Checked = c.OnlyWhenUnfocused;
        _numPort.Value = Math.Clamp(c.Port, 1024, 65535);
        _chkQuiet.Checked = c.Quiet.Enabled;
        _txtQuietStart.Text = c.Quiet.Start;
        _txtQuietEnd.Text = c.Quiet.End;
        _chkAllowAlarms.Checked = c.Quiet.AllowAlarms;
        _txtToken.Text = c.GitHub.Token;
        _txtUser.Text = c.GitHub.Username;
        _numPoll.Value = Math.Clamp(c.GitHub.PollSeconds, 5, 300);
        _txtUrls.Text = string.Join(Environment.NewLine, c.UptimeUrls);
        _chkCountdown.Checked = c.Countdown.Enabled;
        _dtp.Value = c.Countdown.TargetLocal;
        if (_list.Items.Count > 0) _list.Items[0].Selected = true;
    }

    void SaveAll()
    {
        var c = AppConfig.Current;
        c.StartWithWindows = _chkStartup.Checked;
        c.OnlyWhenUnfocused = _chkUnfocused.Checked;
        c.Port = (int)_numPort.Value;
        c.Quiet.Enabled = _chkQuiet.Checked;
        c.Quiet.Start = _txtQuietStart.Text.Trim();
        c.Quiet.End = _txtQuietEnd.Text.Trim();
        c.Quiet.AllowAlarms = _chkAllowAlarms.Checked;
        ApplyGitHub();
        c.UptimeUrls = _txtUrls.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        c.Countdown.Enabled = _chkCountdown.Checked;
        c.Countdown.TargetLocal = _dtp.Value;
        ApplySelectedEvent();
        RefreshList();

        WindowsStartup.SetEnabled(c.StartWithWindows);
        AppConfig.Save();
        if (Server != null && Server.Port != c.Port) Server.Start(c.Port);
        if (GitHub != null && GitHub.PollSeconds != c.GitHub.PollSeconds) GitHub.Restart();
        ApplyWatcherFlag(); // keep the watcher matched to the flag after every save
        RefreshIntegrationStatuses();
        MessageBox.Show(this, "Settings saved.", "Noizes", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        ApplySelectedEvent(); // keep event edits even if Save was not clicked
        base.OnFormClosing(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!FirstRun || _tabs.TabPages.Count == 0) return;
        _tabs.SelectedIndex = 0; // Integrations once it exists; today the first tab
        var hint = new Label
        {
            Text = "Everything is off by default - connect an integration on this tab to hear your first sound.",
            Dock = DockStyle.Top, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 8, 0, 0)
        };
        _tabs.TabPages[0].Controls.Add(hint);
        hint.BringToFront();
    }
}

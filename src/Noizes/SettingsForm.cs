using System.Drawing;
using System.Windows.Forms;

namespace Noizes;

public class SettingsForm : Form
{
    public HttpServer Server;

    // events tab
    readonly ListView _list = new();
    readonly CheckBox _chkEnabled = new() { Text = "Play this event", AutoSize = true };
    readonly TrackBar _vol = new() { TickStyle = TickStyle.None, Minimum = 0, Maximum = 100, Width = 180 };
    readonly Label _lblVol = new() { Text = "80%", Width = 44, TextAlign = ContentAlignment.MiddleLeft };
    readonly TextBox _txtSound = new() { ReadOnly = true, Width = 280 };
    readonly TextBox _txtFocusApps = new() { Width = 300 };
    readonly Button _btnBrowse = new() { Text = "Choose sound..." };
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

    // connections tab
    readonly Button _btnClaude = new() { Text = "Connect Claude Code" };
    readonly Button _btnCursor = new() { Text = "Connect Cursor" };
    readonly TextBox _txtToken = new() { Width = 340, UseSystemPasswordChar = true };
    readonly TextBox _txtUser = new() { Width = 180 };
    readonly Button _btnSaveGh = new() { Text = "Save GitHub settings" };
    readonly Button _btnTestGh = new() { Text = "Test GitHub connection" };
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

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildEventsTab());
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildConnectionsTab());
        tabs.TabPages.Add(BuildExtrasTab());

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 46,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8)
        };
        var btnClose = new Button { Text = "Close" };
        btnClose.Click += (s, e) => Close();
        var btnSave = new Button { Text = "Save settings" };
        btnSave.Click += (s, e) => SaveAll();
        bottom.Controls.Add(btnClose);
        bottom.Controls.Add(btnSave);

        Controls.Add(tabs);
        Controls.Add(bottom);

        LoadAll();
    }

    TabPage BuildEventsTab()
    {
        var page = new TabPage("Events");

        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.Columns.Add("Event", 310);
        _list.Columns.Add("On/Off", 70);
        _list.Columns.Add("Volume", 70);
        _list.Columns.Add("Sound", 260);
        _list.Dock = DockStyle.Fill;
        foreach (var def in EventRegistry.All)
        {
            var ec = AppConfig.Current.Events[def.Id];
            var item = new ListViewItem(new[]
            {
                def.DisplayName,
                ec.Enabled ? "On" : "Off",
                ec.Volume + "%",
                string.IsNullOrEmpty(ec.SoundPath) ? "(default)" : Path.GetFileName(ec.SoundPath)
            });
            item.Tag = def.Id;
            _list.Items.Add(item);
        }
        _list.SelectedIndexChanged += (s, e) => LoadSelectedEvent();

        var detail = new GroupBox { Text = "Selected event", Dock = DockStyle.Bottom, Height = 200, Padding = new Padding(10) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        grid.Controls.Add(new Label { Text = "Enabled:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) }, 0, 0);
        grid.Controls.Add(_chkEnabled, 1, 0);
        grid.Controls.Add(new Label { Text = "Volume:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) }, 0, 1);
        var volPanel = new FlowLayoutPanel { AutoSize = true, Height = 36, Width = 320 };
        volPanel.Controls.Add(_vol);
        volPanel.Controls.Add(_lblVol);
        _vol.Scroll += (s, e) => _lblVol.Text = _vol.Value + "%";
        grid.Controls.Add(volPanel, 1, 1);
        grid.Controls.Add(new Label { Text = "Sound:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) }, 0, 2);
        var soundPanel = new FlowLayoutPanel { AutoSize = true, Height = 36, Width = 600 };
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

        panel.Controls.Add(_chkStartup);
        panel.Controls.Add(_chkUnfocused);

        var portPanel = new FlowLayoutPanel { AutoSize = true, Height = 38, Width = 640 };
        portPanel.Controls.Add(new Label { Text = "Local server port:", AutoSize = true, Margin = new Padding(3, 9, 3, 0) });
        portPanel.Controls.Add(_numPort);
        portPanel.Controls.Add(new Label { Text = "  (hooks call http://127.0.0.1:<port>/event/...)", AutoSize = true, Margin = new Padding(3, 9, 3, 0) });
        panel.Controls.Add(portPanel);

        panel.Controls.Add(new Label { Text = "Quiet hours:", AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
        panel.Controls.Add(_chkQuiet);
        var quietPanel = new FlowLayoutPanel { AutoSize = true, Height = 38, Width = 640, Padding = new Padding(24, 0, 0, 0) };
        quietPanel.Controls.Add(new Label { Text = "From", AutoSize = true, Margin = new Padding(3, 9, 3, 0) });
        quietPanel.Controls.Add(_txtQuietStart);
        quietPanel.Controls.Add(new Label { Text = "to", AutoSize = true, Margin = new Padding(3, 9, 3, 0) });
        quietPanel.Controls.Add(_txtQuietEnd);
        quietPanel.Controls.Add(new Label { Text = "(24-hour HH:MM, e.g. 22:00 and 07:00)", AutoSize = true, Margin = new Padding(3, 9, 3, 0) });
        panel.Controls.Add(quietPanel);
        panel.Controls.Add(_chkAllowAlarms);

        page.Controls.Add(panel);
        return page;
    }

    TabPage BuildConnectionsTab()
    {
        var page = new TabPage("Connections");

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            AutoSize = true, Padding = new Padding(12), WrapContents = false
        };

        var g1 = new GroupBox { Text = "Coding agents", AutoSize = true, Width = 700, Padding = new Padding(10) };
        var g1p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Width = 660, WrapContents = false };
        g1p.Controls.Add(new Label
        {
            Text = "These buttons add Noizes hooks to the tools' own config files. The original file is backed up first\n(a .noizes-backup-... copy next to it) and nothing existing is ever removed.",
            AutoSize = true, Width = 650
        });
        var row1 = new FlowLayoutPanel { AutoSize = true, Height = 40, Width = 650 };
        row1.Controls.Add(_btnClaude);
        g1p.Controls.Add(row1);
        var row2 = new FlowLayoutPanel { AutoSize = true, Height = 40, Width = 650 };
        row2.Controls.Add(_btnCursor);
        g1p.Controls.Add(row2);
        g1.Controls.Add(g1p);

        var g2 = new GroupBox { Text = "GitHub", AutoSize = true, Width = 700, Padding = new Padding(10) };
        var g2p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Width = 660, WrapContents = false };
        g2p.Controls.Add(new Label
        {
            Text = "Paste a read-only personal access token (see README for how to make one). Noizes polls your account\nevery 30 seconds for pushes, PRs, failed checks, stars, issues and comments, plus deployment statuses.",
            AutoSize = true, Width = 650
        });
        var tokRow = new FlowLayoutPanel { AutoSize = true, Height = 38, Width = 650 };
        tokRow.Controls.Add(new Label { Text = "Token:", AutoSize = true, Margin = new Padding(3, 9, 3, 0) });
        tokRow.Controls.Add(_txtToken);
        g2p.Controls.Add(tokRow);
        var userRow = new FlowLayoutPanel { AutoSize = true, Height = 38, Width = 650 };
        userRow.Controls.Add(new Label { Text = "Username:", AutoSize = true, Margin = new Padding(3, 9, 3, 0) });
        userRow.Controls.Add(_txtUser);
        g2p.Controls.Add(userRow);
        var ghRow = new FlowLayoutPanel { AutoSize = true, Height = 40, Width = 650 };
        ghRow.Controls.Add(_btnSaveGh);
        ghRow.Controls.Add(_btnTestGh);
        ghRow.Controls.Add(_lblGhStatus);
        g2p.Controls.Add(ghRow);
        g2.Controls.Add(g2p);

        panel.Controls.Add(g1);
        panel.Controls.Add(g2);
        page.Controls.Add(panel);

        _btnClaude.Click += (s, e) => ConnectClaude();
        _btnCursor.Click += (s, e) => ConnectCursor();
        _btnSaveGh.Click += (s, e) => { ApplyGitHub(); AppConfig.Save(); _lblGhStatus.Text = "Saved."; };
        _btnTestGh.Click += (s, e) => _ = TestGitHub();

        return page;
    }

    TabPage BuildExtrasTab()
    {
        var page = new TabPage("Uptime & countdown");
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            AutoSize = true, Padding = new Padding(12), WrapContents = false
        };

        var g1 = new GroupBox { Text = "Uptime watch", AutoSize = true, Width = 700, Padding = new Padding(10) };
        var g1p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Width = 660, WrapContents = false };
        g1p.Controls.Add(new Label
        {
            Text = "One URL per line. Each URL is checked every 3 minutes; if one fails twice in a row, the\n\"Uptime: a site is down\" alarm plays (even during quiet hours unless you say otherwise).",
            AutoSize = true, Width = 650
        });
        _txtUrls.Width = 650;
        g1p.Controls.Add(_txtUrls);
        g1.Controls.Add(g1p);

        var g2 = new GroupBox { Text = "Countdown", AutoSize = true, Width = 700, Padding = new Padding(10) };
        var g2p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Width = 660, WrapContents = false };
        g2p.Controls.Add(_chkCountdown);
        var dtpRow = new FlowLayoutPanel { AutoSize = true, Height = 38, Width = 650 };
        dtpRow.Controls.Add(new Label { Text = "Target date & time:", AutoSize = true, Margin = new Padding(3, 9, 3, 0) });
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
        _lblVol.Text = _vol.Value + "%";
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
        foreach (ListViewItem item in _list.Items)
        {
            if ((string)item.Tag != _currentEvent) continue;
            item.SubItems[1].Text = ec.Enabled ? "On" : "Off";
            item.SubItems[2].Text = ec.Volume + "%";
            item.SubItems[3].Text = string.IsNullOrEmpty(ec.SoundPath) ? "(default)" : Path.GetFileName(ec.SoundPath);
        }
    }

    void RefreshList()
    {
        foreach (ListViewItem item in _list.Items)
        {
            var ec = AppConfig.Current.Events[(string)item.Tag];
            item.SubItems[1].Text = ec.Enabled ? "On" : "Off";
            item.SubItems[2].Text = ec.Volume + "%";
            item.SubItems[3].Text = string.IsNullOrEmpty(ec.SoundPath) ? "(default)" : Path.GetFileName(ec.SoundPath);
        }
    }

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
        var played = AudioPlayer.Play(_currentEvent + "-test", path, ec.Volume);
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
    }

    async System.Threading.Tasks.Task TestGitHub()
    {
        ApplyGitHub();
        _btnTestGh.Enabled = false;
        _lblGhStatus.Text = "Testing...";
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
        MessageBox.Show(this, "Settings saved.", "Noizes", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        ApplySelectedEvent(); // keep event edits even if Save was not clicked
        base.OnFormClosing(e);
    }
}

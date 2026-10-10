using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Noizes;

/// <summary>
/// UI v2 native host (brief section 2): a WinForms window filled by a WebView2 control that
/// renders the embedded ui/ folder. Replaces SettingsForm everywhere settings open; the old
/// form stays in the repo only as the fallback when the WebView2 runtime is missing.
/// </summary>
public static class SettingsWindow
{
    const string UiHost = "noizes.ui";
    const string RuntimeDownloadUrl = "https://developer.microsoft.com/en-us/microsoft-edge/webview2/";

    // ---------- public contract other lanes code against ----------

    /// <summary>Opens the settings window, or brings the already open one to the front.
    /// Idempotent; the tray (lane C) and any settings launch path call this.</summary>
    public static void Show()
    {
        if (_useLegacy)
        {
            OpenLegacyForm();
            return;
        }
        var f = _form;
        if (f != null && !f.IsDisposed)
        {
            // opening when already open brings the existing window to the front
            if (f.InvokeRequired)
            {
                try { f.BeginInvoke(new Action(Show)); }
                catch (InvalidOperationException ex) { Logger.Info("settings focus race: " + ex.Message); }
                return;
            }
            if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
            f.Activate();
            return;
        }
        _form = new SettingsWindowForm(1.0, false, "");
        _form.FormClosed += (s, e) => { if (ReferenceEquals(_form, s)) _form = null; };
        _form.Show();
    }

    /// <summary>True while the settings window is on screen. The bridge (lane B) uses it to
    /// decide whether pushing state to the page has a listener.</summary>
    public static bool IsOpen => _form != null && !_form.IsDisposed;

    /// <summary>Assigned once by the UI bridge (lane B, UiBridge.cs). Runs with the ready
    /// CoreWebView2 so the bridge can subscribe WebMessageReceived and push state updates.
    /// Fires again on every window reopen with the fresh instance.</summary>
    public static Action<CoreWebView2> OnWebViewReady;

    /// <summary>Assigned by the tray (lane C, TrayService.cs) so the WebView2-missing fallback
    /// opens the legacy form wired to the live server, poller and watcher. When unset the bare
    /// SettingsForm constructor is used (it null-guards its dependencies).</summary>
    public static Func<Form> LegacyFallback;

    // ---------- screenshot evidence mode (brief section 7) ----------

    /// <summary>Renders the real settings window to PNGs at 100 and 150 percent scaling and
    /// returns a process exit code. The 150 percent pass uses the WebView2 device scale factor
    /// browser argument because a CI runner has a single 100 percent display.</summary>
    public static int CaptureScreenshots(string folder)
    {
        Directory.CreateDirectory(folder);
        int code = 0;
        foreach (double scale in new[] { 1.0, 1.5 })
        {
            var f = new SettingsWindowForm(scale, true,
                Path.Combine(folder, "settings-" + (int)Math.Round(scale * 100) + ".png"));
            Application.Run(f);
            if (f.RunExitCode != 0) code = f.RunExitCode;
        }
        return code;
    }

    // ---------- internals ----------

    static SettingsWindowForm _form;
    static Form _legacyForm;
    static bool _useLegacy; // WebView2 missing: every later Show opens the legacy form

    internal static void MarkLegacyOnly() => _useLegacy = true;

    static void OpenLegacyForm()
    {
        try
        {
            if (_legacyForm != null && !_legacyForm.IsDisposed)
            {
                if (_legacyForm.WindowState == FormWindowState.Minimized)
                    _legacyForm.WindowState = FormWindowState.Normal;
                _legacyForm.Activate();
                return;
            }
            var factory = LegacyFallback;
            _legacyForm = factory != null ? factory() : new SettingsForm();
            _legacyForm.FormClosed += (s, e) => { if (ReferenceEquals(_legacyForm, s)) _legacyForm = null; };
            _legacyForm.Show();
        }
        catch (Exception ex) { Logger.Info("legacy settings window failed: " + ex.Message); }
    }

    static string LocalDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Noizes");
    static string UiDir => Path.Combine(LocalDir, "ui");
    static string WebViewDir => Path.Combine(LocalDir, "WebView2");

    private sealed class SettingsWindowForm : Form
    {
        readonly WebView2 _web;
        readonly double _forceScale;
        readonly bool _screenshot;
        readonly string _shotPath;
        bool _captured;
        int _runExitCode;

        public int RunExitCode => _runExitCode;

        public SettingsWindowForm(double forceScale, bool screenshot, string shotPath)
        {
            _forceScale = forceScale;
            _screenshot = screenshot;
            _shotPath = shotPath;

            Text = "Noizes";
            MinimumSize = new Size(960, 640);
            // --bg from the design tokens: no white flash before the page paints
            BackColor = Color.FromArgb(11, 11, 13);
            Icon = LoadAppIcon();

            // size and position come from config Window settings (the field is added by lane B
            // and consumed through a reflection shim so this file builds before that lane lands)
            if (WindowMemory.TryLoad(out var saved))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = saved;
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
                Size = new Size(1080, 720);
            }

            _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
            Controls.Add(_web);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int useDark = 1; // attribute 20 is DWMWA_USE_IMMERSIVE_DARK_MODE
                int hr = DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int));
                if (hr != 0) Logger.Info("dark title bar not applied (hr " + hr + ")");
            }
            catch (Exception ex) { Logger.Info("dark title bar skipped: " + ex.Message); }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_screenshot) StartScreenshotWatchdog();
            InitializeWebView();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!_screenshot) WindowMemory.Save(this);
        }

        async void InitializeWebView()
        {
            try
            {
                ExtractUiAssets();
                var options = new CoreWebView2EnvironmentOptions();
                if (_forceScale > 1.0)
                    options.AdditionalBrowserArguments =
                        "--force-device-scale-factor=" + _forceScale.ToString(CultureInfo.InvariantCulture);
                var env = await CoreWebView2Environment.CreateAsync(null, WebViewDir, options);
                await _web.EnsureCoreWebView2Async(env);
                ConfigureWebview();
                _web.CoreWebView2.Navigate("https://" + UiHost + "/index.html");
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                Logger.Info("webview2 runtime missing: " + ex.Message);
                MarkLegacyOnly();
                if (!_screenshot)
                {
                    ShowRuntimeDialog();
                    OpenLegacyForm();
                }
                Fail("WebView2 runtime is missing");
                Close();
            }
            catch (Exception ex)
            {
                Logger.Info("webview2 init failed, falling back to the legacy window: " + ex.Message);
                if (!_screenshot) OpenLegacyForm();
                Fail("webview init failed: " + ex.Message);
                Close();
            }
        }

        void ConfigureWebview()
        {
            var core = _web.CoreWebView2;

            // the ui folder is reachable only under https://noizes.ui (brief section 2)
            core.SetVirtualHostNameToFolderMapping(UiHost, UiDir, CoreWebView2HostResourceAccessKind.Allow);

            CoreWebView2Settings s = core.Settings;
#if !DEBUG
            // release lockdown: no dev tools, no default right-click menu, no zoom, no status bar
            s.AreDevToolsEnabled = false;
            s.AreDefaultContextMenusEnabled = false;
            s.IsZoomControlEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsStatusBarEnabled = false;
#endif

            // links open in the default browser, never inside the window
            core.NewWindowRequested += (sender, e) =>
            {
                OpenInBrowser(e.Uri);
                e.Handled = true;
            };
            core.NavigationStarting += (sender, e) =>
            {
                if (e.Uri.StartsWith("https://" + UiHost + "/", StringComparison.Ordinal)) return;
                e.Cancel = true; // the page never navigates itself away
                OpenInBrowser(e.Uri);
            };

            SettingsWindow.OnWebViewReady?.Invoke(core);

            if (_screenshot)
                core.NavigationCompleted += (sender, e) => BeginSettleThenCapture();
        }

        // ---------- helpers ----------

        static Icon LoadAppIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
            catch { return SystemIcons.Application; }
        }

        static void OpenInBrowser(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(uri.ToString()) { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex) { Logger.Info("could not open browser for " + url + ": " + ex.Message); }
        }

        /// <summary>Copies the embedded ui resources to %LOCALAPPDATA%\Noizes\ui so the virtual
        /// host can serve them (brief section 2).</summary>
        static void ExtractUiAssets()
        {
            var asm = typeof(SettingsWindow).Assembly;
            const string prefix = "ui/";
            int count = 0;
            foreach (var res in asm.GetManifestResourceNames())
            {
                if (!res.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var rel = res.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar);
                var target = Path.Combine(UiDir, rel);
                var folder = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                try
                {
                    using var src = asm.GetManifestResourceStream(res);
                    if (src == null) continue;
                    using var dst = File.Create(target);
                    src.CopyTo(dst);
                    count++;
                }
                catch (Exception ex)
                {
                    // a stale locked file from a previous run still renders; do not fail over
                    Logger.Info("ui extract skipped " + res + ": " + ex.Message);
                }
            }
            if (count == 0) Logger.Info("no embedded ui resources in this build");
        }

        void Fail(string why)
        {
            _runExitCode = 1;
            if (_screenshot) Console.Error.WriteLine("screenshot run failed: " + why);
        }

        // ---------- screenshot capture ----------

        void StartScreenshotWatchdog()
        {
            var watchdog = new System.Windows.Forms.Timer { Interval = 30000 };
            watchdog.Tick += (sender, e) =>
            {
                var t = (System.Windows.Forms.Timer)sender;
                t.Stop();
                t.Dispose();
                if (!_captured)
                {
                    Logger.Info("screenshot watchdog fired before NavigationCompleted");
                    BeginSettleThenCapture();
                }
            };
            watchdog.Start();
        }

        void BeginSettleThenCapture()
        {
            if (_captured) return; // NavigationCompleted can fire once per navigation
            _captured = true;
            var settle = new System.Windows.Forms.Timer { Interval = 1200 }; // fonts and first paint after load
            settle.Tick += (sender, e) =>
            {
                var t = (System.Windows.Forms.Timer)sender;
                t.Stop();
                t.Dispose();
                SaveScreenshot();
                Close();
            };
            settle.Start();
        }

        void SaveScreenshot()
        {
            try
            {
                TopMost = true; // nothing occludes the capture on a busy desktop
                Activate();
                var b = Bounds;
                using var bmp = new Bitmap(b.Width, b.Height);
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(b.X, b.Y, 0, 0, new Size(b.Width, b.Height));
                bmp.Save(_shotPath, System.Drawing.Imaging.ImageFormat.Png);
                Console.WriteLine("saved " + _shotPath);
            }
            catch (Exception ex)
            {
                _runExitCode = 1;
                Logger.Info("screenshot save failed: " + ex.Message);
                Console.Error.WriteLine("screenshot save failed: " + ex.Message);
            }
        }

        void ShowRuntimeDialog()
        {
            var msg = "Noizes needs the Microsoft WebView2 Runtime to open the new settings window.\n" +
                      "It usually ships with Windows 10 and 11, but this PC does not have it.\n" +
                      "The old settings window will open instead.";
            using var box = new Form
            {
                Text = "Noizes",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(440, 152),
                TopMost = true
            };
            var text = new Label { Text = msg, Location = new Point(16, 12), Size = new Size(408, 72) };
            var link = new LinkLabel
            {
                Text = "Download the WebView2 Runtime from Microsoft",
                Location = new Point(16, 90),
                AutoSize = true
            };
            link.Click += (sender, e) => OpenInBrowser(RuntimeDownloadUrl);
            var ok = new Button
            {
                Text = "Open the old settings window",
                Location = new Point(16, 118),
                AutoSize = true
            };
            ok.Click += (sender, e) => box.Close();
            box.Controls.AddRange(new Control[] { text, link, ok });
            box.ShowDialog();
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }

    /// <summary>
    /// Window size and position memory. The Window field on AppConfig is added by lane B and
    /// this lane must not edit AppConfig.cs, so the field is reached by reflection until the
    /// lanes merge. Contract (docs/DECISIONS.md): AppConfig.Window exposes settable int
    /// properties X, Y, Width, Height. Missing or odd values mean defaults, never a crash.
    /// </summary>
    private static class WindowMemory
    {
        public static bool TryLoad(out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            try
            {
                object window = GetWindowObject();
                if (window == null) return false;
                int w = GetInt(window, "Width");
                int h = GetInt(window, "Height");
                if (w < 100 || h < 100) return false; // field exists but was never written
                var r = new Rectangle(GetInt(window, "X"), GetInt(window, "Y"), w, h);
                if (!Screen.AllScreens.Any(s => r.IntersectsWith(s.Bounds))) return false; // monitor gone
                bounds = r;
                return true;
            }
            catch (Exception ex) { Logger.Info("window memory load skipped: " + ex.Message); return false; }
        }

        public static void Save(Form f)
        {
            try
            {
                if (f.WindowState != FormWindowState.Normal) return; // remember restored bounds only
                object window = GetWindowObject();
                if (window == null) return;
                SetInt(window, "X", f.Left);
                SetInt(window, "Y", f.Top);
                SetInt(window, "Width", f.Width);
                SetInt(window, "Height", f.Height);
                AppConfig.Save();
            }
            catch (Exception ex) { Logger.Info("window memory save skipped: " + ex.Message); }
        }

        static object GetWindowObject()
        {
            // the Window property is lane B's addition to AppConfig; absent until the lanes merge
            return typeof(AppConfig).GetProperty("Window")?.GetValue(AppConfig.Current);
        }

        static int GetInt(object target, string name)
        {
            var p = target.GetType().GetProperty(name);
            if (p == null || !p.CanRead) return 0;
            return p.GetValue(target) is int i ? i : 0;
        }

        static void SetInt(object target, string name, int value)
        {
            target.GetType().GetProperty(name)?.SetValue(target, value);
        }
    }
}

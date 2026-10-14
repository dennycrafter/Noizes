using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
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
            BringToFront(f);
            return;
        }
        _form = new SettingsWindowForm(1.0, false, "");
        _form.FormClosed += (s, e) => { if (ReferenceEquals(_form, s)) _form = null; };
        _form.Show();
        BringToFront(_form);
    }

    /// <summary>Puts the window on top and gives it focus. Windows only lets the app the user is
    /// using take the foreground, so a quick TopMost flip lifts the window above whatever is in
    /// front even when focus is refused, and the window never opens hidden behind other apps.</summary>
    static void BringToFront(Form f)
    {
        try
        {
            if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
            if (!f.Visible) f.Show();
            f.TopMost = true;
            f.TopMost = false;
            f.Activate();
            SetForegroundWindow(f.Handle);
        }
        catch (Exception ex) { Logger.Info("settings bring to front failed: " + ex.Message); }
    }

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Where the window opens. Pure so the self-test can cover every case: a saved spot is
    /// kept only while its title bar is on a screen that still exists, the size never exceeds the
    /// screen, and anything else (first run, the -1 "no position yet" default, an unplugged
    /// monitor, a spot far off screen) centers the window on the main screen.</summary>
    internal static Rectangle PlaceWindow(Rectangle saved, Rectangle[] workAreas, Rectangle primaryWork, Size defaultSize)
    {
        if (primaryWork.Width <= 0 || primaryWork.Height <= 0)
            primaryWork = workAreas != null && workAreas.Length > 0 ? workAreas[0] : new Rectangle(0, 0, 1280, 800);
        bool sizeOk = saved.Width >= 100 && saved.Height >= 100;
        var size = sizeOk ? saved.Size : defaultSize;
        bool noPositionYet = saved.X == -1 && saved.Y == -1;

        Rectangle area = Rectangle.Empty;
        if (sizeOk && !noPositionYet && workAreas != null)
        {
            // the title bar must be grabbable: a point just below the top centre has to be on a screen
            var grip = new Point(saved.X + saved.Width / 2, saved.Y + 16);
            foreach (var a in workAreas)
                if (a.Contains(grip)) { area = a; break; }
        }
        bool keepPosition = !area.IsEmpty;
        if (!keepPosition) area = primaryWork;

        int w = Math.Min(size.Width, area.Width);
        int h = Math.Min(size.Height, area.Height);
        int x = keepPosition ? Math.Clamp(saved.X, area.Left, area.Right - w) : area.Left + (area.Width - w) / 2;
        int y = keepPosition ? Math.Clamp(saved.Y, area.Top, area.Bottom - h) : area.Top + (area.Height - h) / 2;
        return new Rectangle(x, y, w, h);
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
        // evidence runs are headless: mirror the app log to stdout so CI shows it
        Logger.Sink = Console.WriteLine;
        foreach (double scale in new[] { 1.0, 1.5 })
        {
            var f = new SettingsWindowForm(scale, true,
                Path.Combine(folder, "settings-" + (int)Math.Round(scale * 100) + ".png"));
            Application.Run(f);
            if (f.RunExitCode != 0) code = f.RunExitCode;
        }
        Logger.Sink = null;
        return code;
    }

    /// <summary>Noizes.exe --smoke-test: opens the real settings window over the real bridge
    /// (no demo data), checks that settings load, a switch click reaches config.json, every page
    /// renders, no script errors happen and the window sits on screen. Exit code 0 means all
    /// passed. CI runs it on a real Windows machine before anything ships.</summary>
    public static int RunSmokeTest()
    {
        Logger.Sink = Console.WriteLine;
        var f = new SettingsWindowForm(1.0, false, "", smoke: true);
        Application.Run(f);
        Logger.Sink = null;
        return f.RunExitCode;
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

    // one environment per options signature, kept alive for the whole process: re-creating an
    // environment over the same user data folder while the previous browser process is still
    // tearing down loses the race with 0x8007139F ("not in the correct state"), which is how the
    // second capture failed on the CI runner and how quickly reopening settings used to fail
    static CoreWebView2Environment _sharedEnv;
    static string _sharedEnvKey = "";

    internal static async Task<CoreWebView2Environment> GetEnvironmentAsync(double forceScale)
    {
        string key = forceScale.ToString(CultureInfo.InvariantCulture);
        if (_sharedEnv != null && _sharedEnvKey == key) return _sharedEnv;

        var options = new CoreWebView2EnvironmentOptions();
        if (forceScale > 1.0)
            options.AdditionalBrowserArguments =
                "--force-device-scale-factor=" + forceScale.ToString(CultureInfo.InvariantCulture);
        // distinct user data folder per options key so two environments never fight over one folder
        string udf = forceScale > 1.0 ? WebViewDir + "-" + key : WebViewDir;
        _sharedEnv = await CoreWebView2Environment.CreateAsync(null, udf, options);
        _sharedEnvKey = key;
        return _sharedEnv;
    }

    private sealed class SettingsWindowForm : Form
    {
        readonly WebView2 _web;
        readonly double _forceScale;
        readonly bool _screenshot;
        readonly string _shotPath;
        readonly bool _smoke;
        bool _captured;
        bool _smokeStarted;
        readonly List<string> _smokeFails = new();
        int _runExitCode;

        public int RunExitCode => _runExitCode;

        public SettingsWindowForm(double forceScale, bool screenshot, string shotPath, bool smoke = false)
        {
            _forceScale = forceScale;
            _screenshot = screenshot;
            _shotPath = shotPath;
            _smoke = smoke;

            Text = "Noizes";
            MinimumSize = new Size(960, 640);
            // --bg from the design tokens: no white flash before the page paints
            BackColor = Color.FromArgb(11, 11, 13);
            Icon = LoadAppIcon();

            // size and position come from config Window settings, made safe by PlaceWindow so the
            // window can never open off screen or bigger than the screen
            var win = AppConfig.Current.Window ?? new WindowConfig();
            Rectangle[] areas;
            Rectangle primary;
            try
            {
                areas = Screen.AllScreens.Select(sc => sc.WorkingArea).ToArray();
                primary = Screen.PrimaryScreen?.WorkingArea ?? Rectangle.Empty;
            }
            catch (Exception ex)
            {
                Logger.Info("screen list unavailable: " + ex.Message);
                areas = Array.Empty<Rectangle>();
                primary = Rectangle.Empty;
            }
            StartPosition = FormStartPosition.Manual;
            Bounds = PlaceWindow(new Rectangle(win.X, win.Y, win.Width, win.Height), areas, primary, new Size(1080, 720));
            if (win.Maximized && !_screenshot && !_smoke) WindowState = FormWindowState.Maximized;

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
            if (_smoke) StartSmokeWatchdog();
            InitializeWebView();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!_screenshot && !_smoke) WindowMemory.Save(this);
            UiBridge.Detach(PostToPage); // a background push after close must not touch the old core
        }

        /// <summary>Posts bridge JSON to the page on the UI thread (the bridge contract:
        /// background threads marshal before anything reaches the WebView2).</summary>
        void PostToPage(string json)
        {
            try
            {
                if (InvokeRequired) { BeginInvoke(new Action(() => PostToPage(json))); return; }
                _web.CoreWebView2?.PostWebMessageAsJson(json);
            }
            catch (Exception ex) { Logger.Info("bridge post to page failed: " + ex.Message); }
        }

        async void InitializeWebView()
        {
            try
            {
                ExtractUiAssets();
                var env = await GetEnvironmentAsync(_forceScale);
                await _web.EnsureCoreWebView2Async(env);
                if (_screenshot) Logger.Info("webview2 runtime: " + env.BrowserVersionString);
                ConfigureWebview();
                // every mode, evidence runs included, renders over the real bridge: the
                // old demo dataset hid the lost-message bug from every screenshot
                _web.CoreWebView2.Navigate("https://" + UiHost + "/index.html");
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                Logger.Info("webview2 runtime missing: " + ex.Message);
                MarkLegacyOnly();
                if (!_screenshot && !_smoke)
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
                if (!_screenshot && !_smoke) OpenLegacyForm();
                Fail("webview init failed: " + ex.Message);
                Close();
            }
        }

        void ConfigureWebview()
        {
            var core = _web.CoreWebView2;

            // the window hosts the bridge by default: the page's getState request must be
            // answered or it sits at "Loading settings." forever (nothing outside the
            // self-test ever wired the host side, in the real app or in screenshot mode).
            // OnWebViewReady stays as an extension point for hosts that need more.
            core.WebMessageReceived += (s, e) =>
            {
                string json = null;
                try { json = e.TryGetWebMessageAsString(); } // DELIBERATE BUG for the proof run
                catch (Exception ex) { Logger.Info("bridge: could not read a page message: " + ex.Message); }
                if (json == null) return;
                if (_screenshot || _smoke) Logger.Info("bridge page said: " + json); // evidence runs narrate
                UiBridge.HandleMessage(json);
            };
            UiBridge.Attach(PostToPage);

            // the ui folder is reachable only under https://noizes.ui (brief section 2)
            core.SetVirtualHostNameToFolderMapping(UiHost, UiDir, CoreWebView2HostResourceAccessKind.Allow);

            CoreWebView2Settings s = core.Settings;
            // web messaging is the whole bridge; do not trust the default
            s.IsWebMessageEnabled = true;
#if !DEBUG
            // release lockdown: no dev tools, no default right-click menu, no zoom, no status bar
            s.AreDevToolsEnabled = false;
            s.AreDefaultContextMenusEnabled = false;
            s.IsZoomControlEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsStatusBarEnabled = false;
#endif

            // a dead browser or renderer process explains silent bridge loss
            core.ProcessFailed += (s2, e2) =>
                Logger.Info("webview2 process failed: " + e2.ProcessFailedKind);

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

            if (_screenshot || _smoke)
            {
                // collect page errors so a stuck capture or smoke run can name its cause
                core.AddScriptToExecuteOnDocumentCreatedAsync(
                    "window.addEventListener('error',function(e){window.__noizesErrors=(window.__noizesErrors||[]).concat(e.message)});" +
                    "window.addEventListener('unhandledrejection',function(e){window.__noizesErrors=(window.__noizesErrors||[]).concat(String(e.reason))});");
                if (_screenshot) core.NavigationCompleted += (sender, e) => BeginSettleThenCapture();
                else core.NavigationCompleted += (sender, e) => RunSmokeAsync();
            }
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
            if (_smoke) SmokeCheck("window-start", false, why);
        }

        // ---------- smoke test (real bridge, real window) ----------

        static string AnnotationText(string s) =>
            (s ?? "").Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");

        void SmokeCheck(string name, bool ok, string detail)
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (string.IsNullOrEmpty(detail) ? "" : " - " + detail));
            if (ok) return;
            _smokeFails.Add(name);
            // a GitHub annotation, so the failure shows on the PR checks page
            Console.WriteLine("::error title=Smoke test: " + name + "::" + AnnotationText(detail));
        }

        Task<string> Js(string script) => _web.CoreWebView2.ExecuteScriptAsync(script);

        async Task<string> JsText(string script)
        {
            var raw = await Js(script);
            try { return JsonSerializer.Deserialize<string>(raw) ?? ""; }
            catch (JsonException) { return raw ?? ""; }
        }

        async Task<bool> WaitFor(string boolScript, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try { if (await Js(boolScript) == "true") return true; }
                catch (Exception ex) { Logger.Info("smoke probe retry: " + ex.Message); }
                await Task.Delay(250);
            }
            return false;
        }

        void StartSmokeWatchdog()
        {
            var watchdog = new System.Windows.Forms.Timer { Interval = 90000 };
            watchdog.Tick += (sender, e) =>
            {
                var t = (System.Windows.Forms.Timer)sender;
                t.Stop();
                t.Dispose();
                if (_smokeStarted) return;
                SmokeCheck("page-navigation", false, "the settings page never finished loading in 90 seconds");
                _runExitCode = 1;
                Close();
            };
            watchdog.Start();
        }

        async void RunSmokeAsync()
        {
            if (_smokeStarted) return; // NavigationCompleted can fire once per navigation
            _smokeStarted = true;
            try
            {
                // 1. settings load over the real bridge: every sound row shows up
                int expected = EventRegistry.All.Length;
                bool rows = await WaitFor("document.querySelectorAll('.snd-row').length === " + expected, 30000);
                string why = rows ? "" : await JsText(
                    "JSON.stringify({errors:window.__noizesErrors||[],loadError:window.__noizesLoadError||null," +
                    "lastReq:window.__noizesLastRequest||null,lastRes:window.__noizesLastResult||null," +
                    "page:((document.getElementById('page')||{}).innerText||'').slice(0,300)})");
                SmokeCheck("settings-load", rows, rows ? expected + " sounds rendered" : "the sounds never showed up: " + why);

                bool realBridge = await Js("!window.__noizesStubInstalled && !window.__noizesDemoWebview") == "true";
                SmokeCheck("real-bridge", realBridge, realBridge ? "no demo data in play" : "the demo stub was active");

                if (rows)
                {
                    // 2. one click reaches the app and lands in config.json
                    const string id = "gh-push";
                    bool before = AppConfig.Current.Events.TryGetValue(id, out var ev0) && ev0.Enabled;
                    await Js("(function(){var b=document.querySelector('[data-event-id=\"" + id +
                             "\"] button[role=\"switch\"]');if(b)b.click();return !!b;})()");
                    bool flipped = false;
                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < 5000 && !flipped)
                    {
                        await Task.Delay(200);
                        flipped = AppConfig.Current.Events.TryGetValue(id, out var ev1) && ev1.Enabled != before;
                    }
                    bool onDisk = false;
                    try
                    {
                        var saved = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(AppConfig.FilePath));
                        onDisk = saved != null && saved.Events.TryGetValue(id, out var ev2) && ev2.Enabled != before;
                    }
                    catch (Exception ex) { Logger.Info("smoke config read failed: " + ex.Message); }
                    SmokeCheck("switch-saves", flipped && onDisk,
                        "clicked " + id + ": app " + (flipped ? "updated" : "did not update") +
                        ", config.json " + (onDisk ? "saved" : "not saved"));

                    // 3. every page renders real content, never a loading or error line
                    string[] badLines = { "Loading settings", "failed to render", "did not load",
                                          "did not answer", "not available", "could not load" };
                    foreach (var pageId in new[] { "connections", "schedule", "general", "sounds" })
                    {
                        await Js("location.hash = '#" + pageId + "'");
                        await Task.Delay(800);
                        var text = await JsText("(document.getElementById('page')||{}).innerText||''");
                        var bad = badLines.FirstOrDefault(line => text.IndexOf(line, StringComparison.OrdinalIgnoreCase) >= 0);
                        bool ok = bad == null && text.Trim().Length > 40;
                        SmokeCheck("page-" + pageId, ok,
                            bad != null ? "shows \"" + bad + "\"" : text.Trim().Length + " characters of content");
                    }
                }

                // 4. no script errors anywhere
                var errors = await JsText("JSON.stringify(window.__noizesErrors||[])");
                SmokeCheck("no-script-errors", errors == "[]", errors == "[]" ? "none" : errors);

                // 5. the window is on screen, not minimized, not hanging off an edge
                var area = Screen.FromControl(this).WorkingArea;
                var bounds = Bounds;
                const int tol = 12; // Windows 10 and 11 draw invisible resize borders outside the frame
                bool inside = bounds.Left >= area.Left - tol && bounds.Top >= area.Top - tol &&
                              bounds.Right <= area.Right + tol && bounds.Bottom <= area.Bottom + tol;
                SmokeCheck("window-on-screen", Visible && WindowState != FormWindowState.Minimized && inside,
                    "window " + bounds + " inside work area " + area);
            }
            catch (Exception ex)
            {
                SmokeCheck("smoke-run", false, ex.GetType().Name + ": " + ex.Message);
            }
            _runExitCode = _smokeFails.Count == 0 ? 0 : 1;
            Console.WriteLine(_smokeFails.Count == 0
                ? "SMOKE: all checks passed"
                : "SMOKE: " + _smokeFails.Count + " check(s) failed: " + string.Join(", ", _smokeFails));
            if (_smokeFails.Count == 0)
                Console.WriteLine("::notice title=Smoke test passed::Real settings window, real connection: settings loaded, a click saved, 4 pages rendered, no script errors, window on screen");
            Close();
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
            // wait for real content, not just first paint: the bridge pushes state after
            // NavigationCompleted, so a fixed settle captured "Loading settings." on the runner
            var poll = new System.Windows.Forms.Timer { Interval = 250 };
            int tries = 0;
            poll.Tick += async (sender, e) =>
            {
                var t = (System.Windows.Forms.Timer)sender;
                tries++;
                bool ready = false;
                try
                {
                    if (_web.CoreWebView2 != null)
                        ready = await _web.CoreWebView2.ExecuteScriptAsync(
                            "document.querySelector('.snd-row') !== null") == "true";
                }
                catch (Exception ex) { Logger.Info("capture probe retry: " + ex.Message); }
                if (!ready && tries < 40) return; // ~10s ceiling, then capture whatever is there
                t.Stop();
                t.Dispose();
                if (!ready)
                {
                    try
                    {
                        var dom = await _web.CoreWebView2.ExecuteScriptAsync(
                            "JSON.stringify({errors:(window.__noizesErrors||[]).slice(0,5),stub:!!window.__noizesStubInstalled,chrome:typeof window.chrome,webview:typeof (window.chrome&&window.chrome.webview),postFn:typeof (window.chrome&&window.chrome.webview&&window.chrome.webview.postMessage),lastReq:window.__noizesLastRequest||null,lastRes:window.__noizesLastResult||null,probe:(function(){try{window.chrome.webview.postMessage({probe:1});return 'posted';}catch(e){return 'threw:'+e.message;}})(),body:document.body.innerHTML})");
                        Console.WriteLine("capture poll gave up after ~10s without rows; page said: " +
                            (dom.Length > 2000 ? dom.Substring(0, 2000) + "...(truncated)" : dom));
                    }
                    catch (Exception ex) { Console.WriteLine("dom dump failed: " + ex.Message); }
                }
                await Task.Delay(400); // fonts and final paint after the rows arrive
                await SaveScreenshotAsync();
                Close();
            };
            poll.Start();
        }

        async Task SaveScreenshotAsync()
        {
            if (TryCaptureWindow())
            {
                Console.WriteLine("saved " + _shotPath);
                return;
            }
            try
            {
                // CI runners can lack an interactive desktop, so the window shot (CopyFromScreen,
                // title bar included) is best effort; the page itself always captures
                using var ms = new MemoryStream();
                await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
                File.WriteAllBytes(_shotPath, ms.ToArray());
                Console.WriteLine("saved " + _shotPath + " (page capture, no title bar)");
            }
            catch (Exception ex)
            {
                _runExitCode = 1;
                Logger.Info("screenshot save failed: " + ex.Message);
                Console.Error.WriteLine("screenshot save failed: " + ex.Message);
            }
        }

        bool TryCaptureWindow()
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
                return true;
            }
            catch (Exception ex)
            {
                Logger.Info("window capture unavailable: " + ex.Message);
                Console.Error.WriteLine("window capture unavailable: " + ex.Message);
                return false;
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

    /// <summary>Window size and position memory, stored in config Window (X, Y, Width, Height,
    /// Maximized). Reading is done by PlaceWindow, which makes any saved value safe.</summary>
    private static class WindowMemory
    {
        public static void Save(Form f)
        {
            try
            {
                if (f.WindowState == FormWindowState.Minimized) return; // keep the last good spot
                var w = AppConfig.Current.Window ?? (AppConfig.Current.Window = new WindowConfig());
                var b = f.WindowState == FormWindowState.Maximized ? f.RestoreBounds : f.Bounds;
                w.Maximized = f.WindowState == FormWindowState.Maximized;
                w.X = b.X;
                w.Y = b.Y;
                w.Width = b.Width;
                w.Height = b.Height;
                AppConfig.Save();
            }
            catch (Exception ex) { Logger.Info("window memory save skipped: " + ex.Message); }
        }
    }
}

using System.Runtime.InteropServices;

namespace Noizes;

internal static class Program
{
    /// <summary>Signal a second launch sends to the running copy: "open the settings window".</summary>
    internal const string ShowSettingsSignal = "Noizes.ShowSettings";

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);
    const int ASFW_ANY = -1;

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Any(a => a == "--selftest"))
            return SelfTest.Run();

        if (args.Any(a => a == "--setup"))
            return SetupRunner.Run(new SetupRunner.Options());

        // UI v2 (brief section 7): --screenshot-settings <folder> opens the real settings
        // window, saves PNGs at 100 and 150 percent scaling and exits. Runs before the
        // single-instance mutex so an evidence run is never swallowed by a running Noizes.
        int shot = Array.FindIndex(args, a => a == "--screenshot-settings");
        if (shot >= 0)
        {
            string folder = shot + 1 < args.Length ? args[shot + 1] : "";
            if (string.IsNullOrWhiteSpace(folder))
            {
                Console.Error.WriteLine("usage: Noizes.exe --screenshot-settings <folder>");
                return 2;
            }
            ApplicationConfiguration.Initialize();
            AppConfig.Load();
            return SettingsWindow.CaptureScreenshots(folder);
        }

        // --smoke-test: the real settings window over the real bridge with a throwaway config,
        // checked end to end and closed (CI runs it before anything ships)
        if (args.Any(a => a == "--smoke-test"))
        {
            ApplicationConfiguration.Initialize();
            AppConfig.DirOverride = Path.Combine(Path.GetTempPath(), "noizes-smoke-" + Guid.NewGuid().ToString("N"));
            AppConfig.Load();
            return SettingsWindow.RunSmokeTest();
        }

        // --background: started by Windows at sign-in, so stay in the tray. Any other launch
        // (Start menu, desktop shortcut, the installer's last page) means the user wants the window.
        bool background = args.Any(a => a == "--background");

        bool createdNew;
        using var mutex = new System.Threading.Mutex(true, "Noizes.SingleInstance", out createdNew);
        if (!createdNew)
        {
            // already running in the tray: a launch by the user opens its settings window
            if (!background) SignalRunningCopy();
            return 0;
        }
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsSignal);

        ApplicationConfiguration.Initialize();

        bool firstRun = !File.Exists(AppConfig.FilePath);
        AppConfig.Load();
        Logger.Info($"Noizes starting (port {AppConfig.Current.Port})");

        WindowsStartup.SetEnabled(AppConfig.Current.StartWithWindows);

        Application.Run(new TrayApplicationContext(firstRun || !background, showSignal));
        return 0;
    }

    static void SignalRunningCopy()
    {
        // this process was started by the user, so it may hand its right to take the
        // foreground to the running copy; without it the window opens behind other apps
        try { AllowSetForegroundWindow(ASFW_ANY); } catch { }
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowSettingsSignal, out var signal))
                using (signal) signal.Set();
        }
        catch { }
    }
}

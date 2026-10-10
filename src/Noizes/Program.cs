namespace Noizes;

internal static class Program
{
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

        bool createdNew;
        using var mutex = new System.Threading.Mutex(true, "Noizes.SingleInstance", out createdNew);
        if (!createdNew) return 0; // already running

        ApplicationConfiguration.Initialize();

        bool firstRun = !File.Exists(AppConfig.FilePath);
        AppConfig.Load();
        Logger.Info($"Noizes starting (port {AppConfig.Current.Port})");

        WindowsStartup.SetEnabled(AppConfig.Current.StartWithWindows);

        Application.Run(new TrayApplicationContext(firstRun));
        return 0;
    }
}

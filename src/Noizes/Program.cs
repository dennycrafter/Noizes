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

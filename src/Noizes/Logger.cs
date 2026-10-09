namespace Noizes;

public static class Logger
{
    static readonly object _lock = new();
    public static string LogPath => Path.Combine(AppConfig.Dir, "log.txt");

    /// <summary>Test seam: SelfTest captures lines instead of tailing the log file.</summary>
    public static Action<string> Sink;

    public static void Info(string msg)
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(AppConfig.Dir);
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > 512 * 1024)
                {
                    var lines = File.ReadAllLines(LogPath);
                    File.WriteAllLines(LogPath, lines.Skip(Math.Max(0, lines.Length - 2000)));
                }
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}{Environment.NewLine}");
            }
            catch { }
            Sink?.Invoke(msg);
        }
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Noizes;

/// <summary>Checks which process owns the foreground window (for "only play when unfocused").</summary>
public static class FocusCheck
{
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static string ForegroundProcessName()
    {
        try
        {
            var hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return null;
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return null; }
    }

    public static bool IsFocused(string[] appNames)
    {
        if (appNames == null || appNames.Length == 0) return false;
        var fg = ForegroundProcessName();
        if (fg == null) return false;
        foreach (var raw in appNames)
        {
            var a = raw?.Trim();
            if (string.IsNullOrEmpty(a)) continue;
            if (fg.Equals(a, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

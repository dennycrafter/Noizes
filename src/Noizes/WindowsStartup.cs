using Microsoft.Win32;
using System.Windows.Forms;

namespace Noizes;

public static class WindowsStartup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Noizes";

    public static bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(ValueName) != null;
        }
        catch { return false; }
    }

    public static void SetEnabled(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on)
                k.SetValue(ValueName, $"\"{Application.ExecutablePath}\"");
            else
                k.DeleteValue(ValueName, false);
        }
        catch (Exception ex)
        {
            Logger.Info("startup toggle failed: " + ex.Message);
        }
    }
}

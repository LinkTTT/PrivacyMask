using System;
using System.IO;
using System.Security;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class MaskStartup
{
    internal const string TrayArgument = "--tray";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PrivacyMask";

    internal static bool TryRead(out bool enabled)
    {
        enabled = false;
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                if (key != null)
                    enabled = string.Equals(key.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (SecurityException) { return false; }
    }

    internal static bool TrySet(bool enabled)
    {
        try
        {
            if (enabled)
            {
                string command = Command;
                if (command.Length > 260)
                    return false;
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                    key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
                    if (key != null)
                        key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (SecurityException) { return false; }
    }

    private static string Command
    {
        get { return "\"" + Application.ExecutablePath + "\" " + TrayArgument; }
    }
}

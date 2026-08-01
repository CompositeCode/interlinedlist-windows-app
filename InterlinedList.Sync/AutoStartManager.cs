using Microsoft.Win32;

namespace InterlinedList.Sync;

/// <summary>
/// Manages launch-at-login via the per-user HKCU Run key (no elevation, no UAC —
/// deliberately NOT HKLM). The MSI sets this on install; this lets the user toggle
/// it afterward from Settings, keeping one source of truth.
/// </summary>
internal static class AutoStartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "InterlinedListSync";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch { return false; }
    }

    public static void Enable()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key?.SetValue(ValueName, $"\"{exe}\"");
        }
        catch (Exception ex) { SyncLog.Error("Failed to enable autostart.", ex); }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) { SyncLog.Error("Failed to disable autostart.", ex); }
    }

    public static void Apply(bool enabled)
    {
        if (enabled) Enable(); else Disable();
    }
}

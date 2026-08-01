using System.Diagnostics;
using System.Text;

namespace InterlinedList.Sync;

/// <summary>
/// Dependency-light logger for the sync utility (mirrors the main app's AppLog):
/// per-day file under <c>%LocalAppData%\InterlinedList\sync\logs</c>, plus best-effort
/// Windows Event Log entries (source <c>InterlinedListSync</c>, created by the MSI).
/// Never throws.
/// </summary>
internal static class SyncLog
{
    private const string EventSourceName = "InterlinedListSync";
    private static readonly object Gate = new();

    public static string CurrentLogFile =>
        Path.Combine(SyncPaths.LogsDir, $"sync-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    public static void Startup()
    {
        var v = typeof(SyncLog).Assembly.GetName().Version?.ToString() ?? "unknown";
        Info($"──── InterlinedList Sync starting · v{v} · pid {Environment.ProcessId} · {Environment.OSVersion} ────");
    }

    private static void Write(string level, string message, Exception? ex)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        if (ex is not null) line += Environment.NewLine + ex;

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SyncPaths.LogsDir);
                File.AppendAllText(CurrentLogFile, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { /* logging must never crash the app */ }

        if (level != "INFO") TryEventLog(level, line);
        try { Debug.WriteLine(line); } catch { /* ignore */ }
    }

    private static void TryEventLog(string level, string line)
    {
        try
        {
            if (!OperatingSystem.IsWindows() || !EventLog.SourceExists(EventSourceName)) return;
            var type = level == "ERROR" ? EventLogEntryType.Error : EventLogEntryType.Warning;
            EventLog.WriteEntry(EventSourceName, line.Length > 30_000 ? line[..30_000] : line, type);
        }
        catch { /* best effort */ }
    }
}

using System.Diagnostics;
using System.IO;
using System.Text;

namespace InterlinedList.Services;

/// <summary>
/// Minimal, dependency-light diagnostic logger. Its whole reason to exist is
/// that an installed build which crashes on startup otherwise "just doesn't run"
/// with nothing to look at — so this writes a timestamped, per-day log file to
/// <c>%LocalAppData%\InterlinedList\logs</c> (needs no elevation) and, on a
/// best-effort basis, mirrors warnings/errors to the Windows Event Log (source
/// <c>InterlinedList</c>, which the MSI creates while elevated).
///
/// Every path is wrapped so logging can never itself throw and take the app down.
/// </summary>
public static class AppLog
{
    private const string EventSourceName = "InterlinedList";
    private const string EventLogName = "Application";
    private static readonly object Gate = new();

    /// <summary>Directory holding the rolling per-day log files.</summary>
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "InterlinedList", "logs");

    /// <summary>Path of today's log file (rolls at local midnight).</summary>
    public static string CurrentLogFile =>
        Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    /// <summary>Emit a one-time banner so every session boundary is obvious in the file.</summary>
    public static void Startup()
    {
        var asm = typeof(AppLog).Assembly;
        var version = asm.GetName().Version?.ToString() ?? "unknown";
        Info($"──── InterlinedList starting · v{version} · pid {Environment.ProcessId} · " +
             $"{Environment.OSVersion} · base='{AppContext.BaseDirectory}' ────");
    }

    private static void Write(string level, string message, Exception? ex)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        if (ex is not null)
            line += Environment.NewLine + ex;

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(CurrentLogFile, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash the app — swallow disk/permission failures.
        }

        if (level != "INFO")
            TryWriteEventLog(level, line);

        try { Debug.WriteLine(line); } catch { /* ignore */ }
    }

    private static void TryWriteEventLog(string level, string line)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return;

            // Creating a source needs admin; the MSI does it. If it isn't present
            // (e.g. xcopy / dev run without install), skip rather than throw.
            if (!EventLog.SourceExists(EventSourceName))
                return;

            var type = level == "ERROR" ? EventLogEntryType.Error : EventLogEntryType.Warning;
            EventLog.WriteEntry(EventSourceName, Truncate(line, 30_000), type);
        }
        catch
        {
            // Best effort only — a locked-down machine may deny even SourceExists.
        }
    }

    // The Event Log rejects entries longer than ~32 KB.
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>Event Log identifiers, exposed so the installer/tests can reference them.</summary>
    public static (string Source, string Log) EventLogTarget => (EventSourceName, EventLogName);
}

using System.Collections.Concurrent;
using System.Text;

namespace InterlinedList.Contract.Tests;

/// <summary>
/// The deliverable that makes this suite worth running: a report naming the
/// endpoints whose contract moved.
///
/// A red test tells you *something* broke. This tells you *what changed*, in a
/// form you can paste into an issue — which is the gap that let six documented
/// "load-bearing constraints" in CLAUDE.md go stale for six weeks while the live
/// spec grew from 189 paths to 233.
///
/// Written to CONTRACT_DRIFT_REPORT (or the test binary's directory), echoed to
/// the console, and appended to GITHUB_STEP_SUMMARY so a scheduled CI run shows
/// the drift on the run page without downloading an artifact.
/// </summary>
public static class DriftLog
{
    public enum Verdict
    {
        /// <summary>Endpoint answered exactly as the app expects.</summary>
        Ok,

        /// <summary>Status, auth model, or payload shape no longer matches what the app requires. This is drift.</summary>
        Drift,

        /// <summary>
        /// A mismatch between the app and the API that is already known and
        /// tracked, so it is reported without failing the run. Reserved for
        /// mismatches whose fix lives in code this suite must not change — a
        /// permanently-red schedule teaches people to ignore the schedule, and
        /// then new drift goes unnoticed, which is the failure mode this whole
        /// suite exists to prevent.
        /// </summary>
        Known,

        /// <summary>Not exercised this run (no credentials, no sample object, or deliberately unexercised).</summary>
        NotRun,
    }

    public sealed record Entry(string Endpoint, Verdict Verdict, string Detail);

    private static readonly ConcurrentBag<Entry> Entries = new();

    public static void Ok(string endpoint, string detail = "") => Entries.Add(new Entry(endpoint, Verdict.Ok, detail));

    public static void Drift(string endpoint, string detail) => Entries.Add(new Entry(endpoint, Verdict.Drift, detail));

    public static void Known(string endpoint, string detail) => Entries.Add(new Entry(endpoint, Verdict.Known, detail));

    public static void NotRun(string endpoint, string reason) => Entries.Add(new Entry(endpoint, Verdict.NotRun, reason));

    public static IReadOnlyCollection<Entry> Snapshot() => Entries.ToArray();

    public static void Publish()
    {
        var report = Render();
        Console.WriteLine(report);

        var path = Environment.GetEnvironmentVariable("CONTRACT_DRIFT_REPORT");
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(AppContext.BaseDirectory, "contract-drift-report.md");

        try
        {
            File.WriteAllText(path, report);
            Console.WriteLine($"[contract] drift report written to {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[contract] could not write drift report to {path}: {ex.Message}");
        }

        var summary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (!string.IsNullOrWhiteSpace(summary))
        {
            try
            {
                File.AppendAllText(summary, report);
            }
            catch
            {
                // A summary we cannot write is not worth failing a run over.
            }
        }
    }

    public static string Render()
    {
        var entries = Entries
            .GroupBy(e => e.Endpoint, StringComparer.Ordinal)
            // If an endpoint was recorded more than once, the worst verdict wins.
            .Select(g => g.OrderByDescending(e => Severity(e.Verdict)).First())
            .OrderBy(e => e.Endpoint, StringComparer.Ordinal)
            .ToList();

        var drift = entries.Where(e => e.Verdict == Verdict.Drift).ToList();
        var known = entries.Where(e => e.Verdict == Verdict.Known).ToList();
        var ok = entries.Where(e => e.Verdict == Verdict.Ok).ToList();
        var notRun = entries.Where(e => e.Verdict == Verdict.NotRun).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("## Live API contract report");
        sb.AppendLine();
        sb.AppendLine($"- Run at: `{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}Z`");
        sb.AppendLine($"- Endpoints matching the app's expectations: **{ok.Count}**");
        sb.AppendLine($"- Endpoints that drifted: **{drift.Count}**");
        sb.AppendLine($"- Known mismatches (tracked, not failing): **{known.Count}**");
        sb.AppendLine($"- Not exercised this run: **{notRun.Count}**");
        sb.AppendLine();

        if (drift.Count > 0)
        {
            sb.AppendLine("### Drift — these endpoints no longer match what the app expects");
            sb.AppendLine();
            sb.AppendLine("| Endpoint | What changed |");
            sb.AppendLine("| --- | --- |");
            foreach (var e in drift)
                sb.AppendLine($"| `{e.Endpoint}` | {Escape(e.Detail)} |");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("### Drift");
            sb.AppendLine();
            sb.AppendLine("None. Every exercised endpoint still returns the status, accepts the auth model, and carries the fields the app's models require.");
            sb.AppendLine();
        }

        if (known.Count > 0)
        {
            sb.AppendLine("### Known mismatches — reported, already tracked, not failing the run");
            sb.AppendLine();
            sb.AppendLine("| Endpoint | Mismatch |");
            sb.AppendLine("| --- | --- |");
            foreach (var e in known)
                sb.AppendLine($"| `{e.Endpoint}` | {Escape(e.Detail)} |");
            sb.AppendLine();
        }

        if (notRun.Count > 0)
        {
            sb.AppendLine("<details><summary>Not exercised this run</summary>");
            sb.AppendLine();
            sb.AppendLine("| Endpoint | Reason |");
            sb.AppendLine("| --- | --- |");
            foreach (var e in notRun)
                sb.AppendLine($"| `{e.Endpoint}` | {Escape(e.Detail)} |");
            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        if (ok.Count > 0)
        {
            sb.AppendLine("<details><summary>Verified endpoints</summary>");
            sb.AppendLine();
            sb.AppendLine("| Endpoint | Observed |");
            sb.AppendLine("| --- | --- |");
            foreach (var e in ok)
                sb.AppendLine($"| `{e.Endpoint}` | {Escape(e.Detail)} |");
            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static int Severity(Verdict v) => v switch
    {
        Verdict.Drift => 3,
        Verdict.Known => 2,
        Verdict.NotRun => 1,
        _ => 0,
    };

    private static string Escape(string s) =>
        string.IsNullOrWhiteSpace(s) ? "&nbsp;" : s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}

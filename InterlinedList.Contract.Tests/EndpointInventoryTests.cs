using System.Text.RegularExpressions;
using Xunit;

namespace InterlinedList.Contract.Tests;

/// <summary>
/// The anti-staleness mechanism, and the only part of this suite that needs no
/// credentials — it runs everywhere, including on ordinary PRs if someone wires
/// it in.
///
/// It re-derives the set of (verb, path) pairs from the client's own source text
/// on every run and compares it with endpoints.manifest.tsv. Add an endpoint to
/// InterlinedApiClient without classifying it in the manifest, and this fails.
///
/// That is the missing feedback loop. The parity audit found the endpoint
/// inventory by hand with a grep, wrote the result into a document, and the
/// document went stale the moment the client grew. Here the grep is the test.
///
/// It joins the live collection purely for ordering: the collection fixture's
/// teardown is what publishes the drift report, so these tests must finish
/// before it runs or their findings would miss the report.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class EndpointInventoryTests
{
    private static readonly Regex PathLiteral = new("\"\\$?(api/[^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex HttpVerb = new(@"HttpMethod\.(Get|Post|Put|Patch|Delete)", RegexOptions.Compiled);
    private static readonly Regex Interpolation = new(@"\{Uri\.EscapeDataString\(([A-Za-z0-9_]+)\)\}", RegexOptions.Compiled);
    private static readonly Regex GetHelper = new(@"\bGet[A-Za-z]*Async\s*[<(]", RegexOptions.Compiled);

    /// <summary>
    /// A helper whose NAME carries its verb — <c>Get…Async</c>, <c>Put…Async</c>,
    /// <c>Post…Async</c>, <c>Patch…Async</c>, <c>Delete…Async</c>.
    /// </summary>
    /// <remarks>
    /// Only consulted after <see cref="HttpVerb"/> fails, so a helper that takes
    /// an explicit <c>HttpMethod</c> always wins and this can never override it.
    /// Matching the convention rather than a hand-kept list matters: the client
    /// grows domain helpers that delegate to the shared ones
    /// (<c>GetSettingsDocumentOrNullAsync</c>, <c>PutSettingsAsync</c>), and each
    /// one would otherwise appear as a new UNKNOWN.
    /// </remarks>
    private static readonly Regex VerbPrefixedHelper =
        new(@"\b(Get|Put|Post|Patch|Delete)[A-Za-z]*Async\s*[<(]", RegexOptions.Compiled);

    [Fact]
    public void Manifest_classifies_exactly_the_endpoints_the_client_calls()
    {
        var fromSource = DiscoverFromClientSources();
        var fromManifest = Manifest.Load().Select(e => $"{e.Method} {e.Path}").ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(fromSource);

        // An unresolved verb means the scanner met a call shape it does not
        // understand. Say so directly instead of letting it surface as a
        // confusing "UNKNOWN api/..." manifest mismatch.
        var unresolved = fromSource.Where(e => e.StartsWith("UNKNOWN ", StringComparison.Ordinal)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.True(
            unresolved.Count == 0,
            "The endpoint scanner could not determine the HTTP verb for these call sites. Teach ResolveVerb about the call shape:\n  " +
            string.Join("\n  ", unresolved));

        var unclassified = fromSource.Except(fromManifest, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var stale = fromManifest.Except(fromSource, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

        if (unclassified.Count > 0)
            DriftLog.Drift("endpoint inventory", $"client calls {unclassified.Count} endpoint(s) missing from the manifest: {string.Join(", ", unclassified)}");
        if (stale.Count > 0)
            DriftLog.Drift("endpoint inventory", $"manifest lists {stale.Count} endpoint(s) the client no longer calls: {string.Join(", ", stale)}");

        Assert.True(
            unclassified.Count == 0,
            "InterlinedApiClient calls endpoints that endpoints.manifest.tsv does not classify. " +
            "Add each one with a coverage class (read/write/skip) and, for skip, the reason it must not be exercised live:\n  " +
            string.Join("\n  ", unclassified));

        Assert.True(
            stale.Count == 0,
            "endpoints.manifest.tsv lists endpoints InterlinedApiClient no longer calls. Remove them:\n  " +
            string.Join("\n  ", stale));
    }

    [Fact]
    public void Every_read_endpoint_in_the_manifest_has_a_probe()
    {
        var declaredReads = Manifest.Load()
            .Where(e => e.Coverage == "read")
            .Select(e => $"{e.Method} {e.Path}")
            .ToHashSet(StringComparer.Ordinal);

        var probes = ReadProbes.All.Keys.ToHashSet(StringComparer.Ordinal);

        var missingProbe = declaredReads.Except(probes, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var orphanProbe = probes.Except(declaredReads, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.True(
            missingProbe.Count == 0,
            "These endpoints are classified 'read' in the manifest but have no probe in ReadProbes.All:\n  " +
            string.Join("\n  ", missingProbe));

        Assert.True(
            orphanProbe.Count == 0,
            "These probes exist in ReadProbes.All but are not classified 'read' in the manifest:\n  " +
            string.Join("\n  ", orphanProbe));
    }

    [Fact]
    public void Every_unexercised_endpoint_states_why()
    {
        var unjustified = Manifest.Load()
            .Where(e => e.Coverage == "skip" && string.IsNullOrWhiteSpace(e.Note))
            .Select(e => $"{e.Method} {e.Path}")
            .ToList();

        Assert.True(
            unjustified.Count == 0,
            "An endpoint may be left unexercised, but not unexplained. Add a reason to endpoints.manifest.tsv for:\n  " +
            string.Join("\n  ", unjustified));
    }

    [Fact]
    public void Coverage_summary_is_reported()
    {
        var byCoverage = Manifest.Load()
            .GroupBy(e => e.Coverage, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var total = byCoverage.Values.Sum();
        var summary = string.Join(", ", byCoverage.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

        DriftLog.Ok("endpoint inventory", $"{total} (verb, path) operations across {Manifest.Load().Select(e => e.Path).Distinct(StringComparer.Ordinal).Count()} path templates — {summary}");

        // A suite that exercises nothing is not a contract suite.
        Assert.True(byCoverage.GetValueOrDefault("read") > 30, $"expected 30+ live read assertions, found {byCoverage.GetValueOrDefault("read")}");
        Assert.True(byCoverage.GetValueOrDefault("write") > 10, $"expected 10+ live write assertions, found {byCoverage.GetValueOrDefault("write")}");
    }

    /// <summary>
    /// The grep, as code. Reads the client partials copied next to the test
    /// binary and extracts every (verb, normalised path) pair.
    /// </summary>
    private static HashSet<string> DiscoverFromClientSources()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "ClientSources");
        Assert.True(Directory.Exists(dir), $"client sources were not copied to {dir}; check the csproj's None/CopyToOutputDirectory item");

        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(dir, "InterlinedApiClient*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var match = PathLiteral.Match(lines[i]);
                if (!match.Success)
                    continue;

                var path = Normalize(match.Groups[1].Value);
                found.Add($"{ResolveVerb(lines, i)} {path}");
            }
        }

        return found;
    }

    /// <summary>
    /// Resolve the verb from the statement that CONTAINS the path literal, and
    /// only that statement.
    ///
    /// A naive fixed look-back of a few lines is wrong, and wrong in a way that
    /// silently mislabels endpoints. InterlinedApiClient.Follow.cs is a stack of
    /// one-line expression-bodied members:
    ///
    ///     public Task UnfollowAsync(...)      =&gt; SendVoidAsync(HttpMethod.Delete, $"api/follow/{userId}", ...);
    ///     public Task&lt;FollowStatus&gt; GetFollowStatusAsync(...) =&gt; GetJsonAsync&lt;FollowStatus&gt;($"api/follow/{userId}/status", ...);
    ///
    /// Looking back three lines from the /status literal finds the *previous*
    /// member's HttpMethod.Delete and records "DELETE api/follow/{userId}/status",
    /// an endpoint that does not exist. So: walk back only to the start of the
    /// current statement, then match within it.
    /// </summary>
    private static string ResolveVerb(string[] lines, int index)
    {
        var statement = ReadStatementUpTo(lines, index);

        var verb = HttpVerb.Match(statement);
        if (verb.Success)
            return verb.Groups[1].Value.ToUpperInvariant();

        // Helpers that imply their verb. SendMultipartAsync is always a POST.
        // Everything else reaching a path literal without an explicit HttpMethod
        // goes through one of the client's Get*Async read helpers
        // (GetJsonAsync / GetElementAsync / GetStringAsync / GetUserArrayAsync),
        // so match that naming convention rather than a hand-kept list — a new
        // read helper should not silently become an UNKNOWN.
        if (statement.Contains("SendMultipartAsync", StringComparison.Ordinal))
            return "POST";
        if (GetHelper.IsMatch(statement))
            return "GET";
        var statementHelper = VerbPrefixedHelper.Match(statement);
        if (statementHelper.Success)
            return statementHelper.Groups[1].Value.ToUpperInvariant();

        // The statement alone wasn't enough. Widen to the ENCLOSING MEMBER and
        // try again.
        //
        // Three real shapes need this, all introduced as the client grew:
        //
        //   1. Path hoisted into a local, verb used later:
        //        var path = $"api/dm/conversations?take={…}";
        //        …
        //        var json = await GetElementAsync(path, ct);
        //
        //   2. Path in a const, used by several methods:
        //        private const string MaterializePath = "api/materialize";
        //
        //   3. A multi-line call whose argument line ends in '}', which
        //      ReadStatementUpTo reads as a statement boundary:
        //        => GetJsonAsync<NewsWidget>(
        //               source is { Length: > 0 }        <-- looks like a boundary
        //                   ? $"api/widgets/news?source={…}"
        //                   : "api/widgets/news",
        //
        // Widening to the member — not to a fixed number of lines — is what
        // keeps this safe. A fixed look-back is what mislabelled adjacent
        // one-line members in Follow.cs (see the remarks above); member bounds
        // cannot cross into a neighbour.
        var member = ReadEnclosingMember(lines, index);

        var memberVerb = HttpVerb.Match(member);
        if (memberVerb.Success)
            return memberVerb.Groups[1].Value.ToUpperInvariant();
        if (member.Contains("SendMultipartAsync", StringComparison.Ordinal))
            return "POST";
        if (GetHelper.IsMatch(member))
            return "GET";
        var memberHelper = VerbPrefixedHelper.Match(member);
        if (memberHelper.Success)
            return memberHelper.Groups[1].Value.ToUpperInvariant();

        // A const shared by several methods has no verb in its own member, so
        // fall back to how the const NAME is used elsewhere in the file.
        var constName = ConstDeclaration.Match(lines[index]);
        if (constName.Success)
        {
            var name = constName.Groups[1].Value;
            foreach (var line in lines)
            {
                if (!line.Contains(name, StringComparison.Ordinal)) continue;
                var useVerb = HttpVerb.Match(line);
                if (useVerb.Success) return useVerb.Groups[1].Value.ToUpperInvariant();
                if (GetHelper.IsMatch(line)) return "GET";
            }
        }

        return "UNKNOWN";
    }

    /// <summary>
    /// Matches <c>private const string SomeName = "api/…"</c> so a path held in
    /// a constant can be resolved from how that constant is used.
    /// </summary>
    private static readonly Regex ConstDeclaration =
        new(@"const\s+string\s+(\w+)\s*=", RegexOptions.Compiled);

    /// <summary>
    /// The text of the member (method / property / field) containing
    /// <paramref name="index"/>.
    /// </summary>
    /// <remarks>
    /// Bounds are found by scanning out to the nearest member declaration or
    /// blank line, and are deliberately narrow: widening to the whole file
    /// would let one method's verb resolve another's path, which is exactly the
    /// class of silent mislabelling this scanner exists to avoid.
    /// </remarks>
    private static string ReadEnclosingMember(string[] lines, int index)
    {
        static bool IsMemberStart(string line)
        {
            var t = line.Trim();
            if (t.Length == 0) return true;
            return (t.StartsWith("public ", StringComparison.Ordinal)
                    || t.StartsWith("private ", StringComparison.Ordinal)
                    || t.StartsWith("internal ", StringComparison.Ordinal)
                    || t.StartsWith("protected ", StringComparison.Ordinal))
                   && !t.StartsWith("private static readonly Regex", StringComparison.Ordinal);
        }

        var start = index;
        while (start > 0 && !IsMemberStart(lines[start])) start--;

        var end = index;
        while (end < lines.Length - 1)
        {
            end++;
            if (IsMemberStart(lines[end])) { end--; break; }
        }

        return string.Join(" ", lines.Skip(start).Take(end - start + 1));
    }

    /// <summary>
    /// Text from the start of the statement containing <paramref name="index"/>
    /// up to and including that line. A statement boundary is the previous line
    /// ending in ';', '{' or '}', or a comment line.
    /// </summary>
    private static string ReadStatementUpTo(string[] lines, int index)
    {
        var start = index;
        while (start > 0)
        {
            var previous = lines[start - 1].Trim();
            if (previous.Length == 0
                || previous.StartsWith("//", StringComparison.Ordinal)
                || previous.EndsWith(";", StringComparison.Ordinal)
                || previous.EndsWith("{", StringComparison.Ordinal)
                || previous.EndsWith("}", StringComparison.Ordinal))
                break;

            start--;
        }

        return string.Join(" ", lines.Skip(start).Take(index - start + 1));
    }

    private static string Normalize(string path)
    {
        var withoutQuery = path.Split('?')[0];
        var collapsed = Interpolation.Replace(withoutQuery, "{$1}");
        return collapsed.TrimEnd('/');
    }
}

internal static class Manifest
{
    internal sealed record Endpoint(string Method, string Path, string Coverage, string Note);

    private static List<Endpoint>? _cached;

    internal static List<Endpoint> Load()
    {
        if (_cached is not null)
            return _cached;

        var path = Path.Combine(AppContext.BaseDirectory, "endpoints.manifest.tsv");
        Assert.True(File.Exists(path), $"endpoints.manifest.tsv was not copied to {path}");

        var endpoints = new List<Endpoint>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            var parts = line.Split('\t');
            Assert.True(parts.Length >= 3, $"malformed manifest row (needs at least METHOD, PATH, COVERAGE): {line}");
            endpoints.Add(new Endpoint(
                parts[0].Trim(),
                parts[1].Trim(),
                parts[2].Trim(),
                parts.Length > 3 ? parts[3].Trim() : string.Empty));
        }

        var duplicates = endpoints
            .GroupBy(e => $"{e.Method} {e.Path}", StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, $"duplicate manifest rows: {string.Join(", ", duplicates)}");

        return _cached = endpoints;
    }
}

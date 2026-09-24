using System.Net.Http;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// AI integration (/api/ai/*). Two-step by design: POST /api/ai/suggest runs a
/// feature and returns a validated preview <b>artifact</b> without writing
/// anything, then POST /api/ai/generate takes that (optionally user-edited)
/// artifact back and persists it, returning the created resource id(s).
/// GET /api/ai/status reports subscriber state, whether the site has AI
/// configured, and the remaining daily quota.
///
/// Live-verified 2026-09-16 with a bearer sync-token on the test account:
///  • GET /api/ai/status → 200 {"subscriber":true,"providers":["anthropic"],
///    "defaultModels":{"anthropic":"claude-sonnet-5","openai":"gpt-4.1-mini",
///    "gemini":"gemini-2.0-flash"},"quota":{"usedToday":0,"dailyLimit":50,
///    "remaining":50}}
///  • POST /api/ai/suggest (writing_assist / rewrite) → 200 {"ok":true,
///    "feature":"writing_assist","artifact":{"kind":"message","content":"…"},
///    "usage":{"inputTokens":125,"outputTokens":40,"model":"claude-sonnet-5"},
///    "quota":{"usedToday":1,"dailyLimit":50}}  ← note: NO "remaining" here,
///    unlike /status.
///  • POST /api/ai/suggest (writing_assist / thread) → artifact
///    {"kind":"thread","parts":[…]}
///  • Error envelope is {"error":"…","code":"…"} on every failure
///    (401 {"error":"Unauthorized","code":"unauthorized"};
///     422 {"error":"Unknown or missing feature.","code":"invalid_input"}).
///  • Input-validation rejections cost NO quota (usedToday stayed 0 across two
///    422s), but a call that reaches the model does — including one that fails.
///
/// <b>Deliberately NOT called:</b> POST /api/ai/generate. It persists real
/// content (lists/documents/folders/scheduled posts) to a shared test account,
/// so <see cref="AiGenerateResult"/> is transcribed from the published contract
/// rather than proven, and callers should re-fetch the created resource — the
/// repo's read-after-write rule — instead of trusting those fields.
///
/// Quota discipline: 50 generations per rolling 24h, and a /suggest plus its
/// /generate are two of them. Failed attempts count too, so nothing here
/// retries on its own; everything cheaply checkable (feature word caps, the
/// 10-word series gate, a missing powered_document source reference, a
/// non-persistable artifact) is rejected client-side as
/// <see cref="AiApiException.InvalidInput"/> before a unit is spent.
/// </summary>
public sealed partial class InterlinedApiClient
{
    /// <summary>
    /// GET /api/ai/status. Any authenticated user may read this — subscriber
    /// gating applies to /suggest and /generate, not here — so it's the right
    /// call to gate whether AI UI is offered at all.
    /// </summary>
    public async Task<AiStatus> GetAiStatusAsync(CancellationToken ct = default)
    {
        var root = await SendAiAsync(HttpMethod.Get, "api/ai/status", body: null, ct);
        return root.Deserialize<AiStatus>(AiJson.Options)
               ?? throw new AiApiException(200, AiErrorCode.Unknown, "GET /api/ai/status returned no body.");
    }

    /// <summary>
    /// POST /api/ai/suggest — run a feature and return a validated preview
    /// artifact. Writes nothing, but spends one daily generation.
    /// </summary>
    /// <param name="feature">Which capability runs; also fixes the artifact kind you get back.</param>
    /// <param name="input">Free-text instruction, or the draft for writing_assist. Word-capped per feature.</param>
    /// <param name="context">Feature-specific, id-based hints. Build with the <see cref="AiContext"/> factories.</param>
    /// <param name="model">Optional Anthropic model id override; defaults to AiStatus.DefaultModel server-side.</param>
    /// <param name="maxOutputTokens">Requested budget. Clamped down to the feature's ceiling here and again server-side.</param>
    public async Task<AiSuggestResult> AiSuggestAsync(
        AiFeature feature,
        string input,
        AiContext? context = null,
        string? model = null,
        int? maxOutputTokens = null,
        CancellationToken ct = default)
    {
        if (!AiFeatureLimits.TryValidateInput(feature, input, out var inputError))
            throw AiApiException.InvalidInput(inputError!);

        if (context is not null && !context.TryValidate(feature, out var contextError))
            throw AiApiException.InvalidInput(contextError!);

        var body = new Dictionary<string, object?>
        {
            ["feature"] = feature.ToWire(),
            ["input"] = input
        };

        // Only send optional fields that were actually set — the shared
        // JsonSerializerOptions write nulls, and an explicit null context/model
        // is a different thing from an absent one.
        if (context is not null)
        {
            var wireContext = context.ToWire(feature);
            if (wireContext.Count > 0) body["context"] = wireContext;
        }

        if (model is { Length: > 0 }) body["model"] = model;

        if (maxOutputTokens is { } requested)
            body["maxOutputTokens"] = AiFeatureLimits.ClampOutputTokens(feature, requested);

        var root = await SendAiAsync(HttpMethod.Post, "api/ai/suggest", body, ct);

        if (root.ObjectOrNull("artifact") is not { } artifact)
            throw new AiApiException(200, AiErrorCode.InvalidAiOutput,
                "POST /api/ai/suggest returned no artifact.");

        return new AiSuggestResult
        {
            Ok = root.BoolOrFalse("ok"),
            Feature = root.StringOrNull("feature") ?? feature.ToWire(),
            Artifact = AiArtifact.FromJson(artifact),
            Usage = root.ObjectOrNull("usage") is { } usage
                ? usage.Deserialize<AiUsage>(AiJson.Options)
                : null,
            Quota = ReadQuota(root),
            Raw = root
        };
    }

    /// <summary>
    /// POST /api/ai/generate — persist a confirmed artifact from
    /// <see cref="AiSuggestAsync"/>. The server re-validates the artifact
    /// (defense in depth) and writes it under the authenticated user; nothing in
    /// the artifact controls ownership.
    ///
    /// Not live-exercised (it writes to a shared test account) — treat the
    /// returned ids as a hint and re-fetch the created resource to confirm.
    /// </summary>
    /// <param name="feature">The feature the artifact was produced for.</param>
    /// <param name="artifact">The confirmed artifact. Must be persistable — the writing_assist kinds are refused locally.</param>
    /// <param name="provider">Recorded in the audit ledger only. AI is Anthropic-only app-wide; this does not select a provider.</param>
    /// <param name="model">Recorded for auditing only; does not affect the write.</param>
    /// <param name="scheduleImmediately">message_series only: create individual scheduled posts instead of a list.</param>
    /// <param name="crossPost">message_series only: the composer's cross-post selection, applied to every scheduled message.</param>
    public async Task<AiGenerateResult> AiGenerateAsync(
        AiFeature feature,
        AiArtifact artifact,
        string? provider = null,
        string? model = null,
        bool scheduleImmediately = false,
        AiCrossPostOptions? crossPost = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        // The three writing_assist kinds (message/thread/tags) are composer
        // insertions, not content: /generate answers 422 invalid_input for them.
        // Refuse locally so a caller can't spend a quota unit finding out.
        if (!artifact.IsPersistable)
            throw AiApiException.InvalidInput(
                $"A \"{artifact.Kind}\" artifact can't be saved — insert it into the composer instead.");

        if (artifact.KnownKind is { } kind && !kind.IsValidFor(feature))
            throw AiApiException.InvalidInput(
                $"A \"{artifact.Kind}\" artifact doesn't belong to {feature.DisplayName()}.");

        var body = new Dictionary<string, object?>
        {
            ["feature"] = feature.ToWire(),
            ["artifact"] = artifact.Raw
        };

        if (provider is { Length: > 0 }) body["provider"] = provider;
        if (model is { Length: > 0 }) body["model"] = model;

        // Both are documented as message_series-only and ignored elsewhere;
        // don't send them for other features rather than rely on that.
        if (feature == AiFeature.MessageSeries)
        {
            if (scheduleImmediately) body["scheduleImmediately"] = true;

            if (crossPost is { HasAnySelection: true })
                body["crossPost"] = crossPost.ToWire();
        }

        var root = await SendAiAsync(HttpMethod.Post, "api/ai/generate", body, ct);

        var created = root.ObjectOrNull("created");
        return new AiGenerateResult
        {
            Ok = root.BoolOrFalse("ok"),
            Feature = root.StringOrNull("feature") ?? feature.ToWire(),
            Created = created is { } createdElement
                ? AiCreatedResources.FromJson(createdElement)
                : new AiCreatedResources(),
            Quota = ReadQuota(root),
            Raw = root
        };
    }

    private static AiQuota ReadQuota(JsonElement root)
        => root.ObjectOrNull("quota") is { } quota
            ? quota.Deserialize<AiQuota>(AiJson.Options) ?? new AiQuota()
            : new AiQuota();

    /// <summary>
    /// The AI equivalent of the shared JSON plumbing. It doesn't reuse
    /// EnsureSuccessAsync because that throws <see cref="InterlinedApiException"/>,
    /// which drops the machine-readable "code" (and the Retry-After hint) these
    /// endpoints rely on — see <see cref="AiApiException"/>. Bodies are parsed
    /// leniently: AI output is long-form markdown, and this API has been observed
    /// emitting text a strict RFC-8259 parser rejects.
    /// </summary>
    private async Task<JsonElement> SendAiAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var resp = await SendAsync(method, path, body, ct);

        if (!resp.IsSuccessStatusCode)
            throw await AiApiException.FromResponseAsync(resp, ct);

        var text = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text))
            throw new AiApiException((int)resp.StatusCode, AiErrorCode.Unknown, $"{method} {path} returned no body.");

        try
        {
            return AiJson.ParseLenient(text);
        }
        catch (JsonException ex)
        {
            throw new AiApiException((int)resp.StatusCode, AiErrorCode.InvalidAiOutput,
                $"{method} {path} returned a body that could not be parsed as JSON: {ex.Message}");
        }
    }
}

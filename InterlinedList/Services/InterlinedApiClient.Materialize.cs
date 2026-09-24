using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// "Create from…" (POST /api/materialize): turn existing messages, lists, rows,
/// a document, or a document selection into a new list, a new document, both, or
/// a post draft. Subscriber-only, mirroring the list/document create gates.
///
/// Two entry points on purpose. <see cref="MaterializeAsync"/> creates and returns
/// ids; <see cref="MaterializeMessageDraftAsync"/> writes nothing and returns a
/// <see cref="MessageDraft"/>. Same endpoint, genuinely different results —
/// conflating them behind one nullable-everything return type would hide which of
/// the two actually happened.
///
/// The request is id-only in both cases (see <see cref="MaterializeSource"/>): the
/// server re-fetches and re-authorizes every referenced id under the calling user,
/// so client cell values and body text are never trusted.
///
/// Verification status (probed live 2026-09-16 with the test account's bearer
/// sync-token; the account is a subscriber, maxMessageLength 666):
/// <list type="bullet">
///   <item>target=message — fully verified, including threading and charLimit sizing.
///         Returns <c>201</c> with <c>{ message: { content, thread, isThread, charLimit } }</c>.</item>
///   <item>404 / 400 / 401 paths — verified (bogus ids, missing source, no token).</item>
///   <item>target=list|doc|both — deliberately NOT exercised: they create real content
///         on shared test infrastructure. The envelope is parsed leniently and kept raw.
///         Their auth/source gating *was* confirmed safely: a bogus source id returns
///         404 and creates nothing (list/document counts unchanged before/after).</item>
///   <item>403 subscription_required — not reproducible with a subscriber account;
///         classification is by status code, which needs no live sample.</item>
/// </list>
/// </summary>
public sealed partial class InterlinedApiClient
{
    private const string MaterializePath = "api/materialize";

    /// <summary>
    /// Create a list, a document, or both from an existing object. On failure the
    /// thrown <see cref="InterlinedApiException"/> is classifiable with
    /// <see cref="MaterializeError.Classify(InterlinedApiException)"/> — notably
    /// <c>403</c> (subscriber gate) and <c>404</c> (id gone or not yours).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// When <paramref name="request"/> targets <see cref="MaterializeTarget.Message"/>,
    /// which creates nothing — use <see cref="MaterializeMessageDraftAsync"/>.
    /// </exception>
    public async Task<MaterializeResult> MaterializeAsync(MaterializeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Target == MaterializeTarget.Message)
            throw new ArgumentException(
                "target=\"message\" writes nothing and returns a draft — call MaterializeMessageDraftAsync instead.",
                nameof(request));

        using var resp = await SendAsync(HttpMethod.Post, MaterializePath, request, ct);
        await EnsureSuccessAsync(resp, ct);

        // Lenient on purpose: this envelope has never been seen live, so a renamed
        // or missing key leaves the property null instead of throwing, and Raw keeps
        // everything for a caller that needs to look. Re-fetch by id for the truth.
        var root = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        return new MaterializeResult
        {
            List = ReadCreatedRef(root, "list"),
            Document = ReadCreatedRef(root, "document"),
            Raw = root
        };
    }

    /// <summary>
    /// Derive a post draft from an existing object. Writes nothing — publish the
    /// result with <see cref="PostMessageAsync"/>, which is where the posting gates
    /// live.
    /// <para>
    /// An over-limit body is rejected with <c>400</c> unless
    /// <see cref="MaterializeMessageConfig.AllowThread"/> is set, in which case
    /// <see cref="MessageDraft.Thread"/> comes back split in reply order. The
    /// rejection message is display-ready; see <see cref="MaterializeError.Describe"/>.
    /// </para>
    /// </summary>
    public async Task<MessageDraft> MaterializeMessageDraftAsync(
        MaterializeSource source,
        MaterializeMessageConfig? messageConfig = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var request = new MaterializeRequest
        {
            Target = MaterializeTarget.Message,
            Source = source,
            MessageConfig = messageConfig
        };

        using var resp = await SendAsync(HttpMethod.Post, MaterializePath, request, ct);
        await EnsureSuccessAsync(resp, ct);

        var root = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        if (!root.TryGetProperty("message", out var draft) || draft.ValueKind != JsonValueKind.Object)
            throw new InterlinedApiException((int)resp.StatusCode,
                "POST /api/materialize (target=message) returned no message draft.");

        return draft.Deserialize<MessageDraft>(JsonOptions)
            ?? throw new InterlinedApiException((int)resp.StatusCode,
                "POST /api/materialize (target=message) returned an unreadable message draft.");
    }

    private static MaterializeCreatedRef? ReadCreatedRef(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var el)
        && el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty("id", out var idEl)
        && idEl.GetString() is { Length: > 0 } id
            ? new MaterializeCreatedRef
            {
                Id = id,
                Title = el.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null
            }
            : null;
}

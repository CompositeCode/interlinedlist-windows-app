using System.Text.Json;
using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// One row of <c>GET /api/user/notification-preferences</c> ("events"): a
/// notifiable event and which delivery channels are enabled for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Each event supports a different subset of channels</b>, and the set is
/// server-defined — not a fixed shape. Verified live 2026-09-16:
/// </para>
/// <code>
/// dig                   push, inApp
/// push                  push, inApp
/// follow                push, email          ← no inApp
/// mention               push, inApp, email
/// reply                 email                ← email ONLY
/// direct_message        push, inApp
/// share                 inApp, email         ← no push
/// integration_reconnect push, inApp
/// </code>
/// <para>
/// The server <b>rejects a channel the event does not support</b> with
/// <c>400 {"error":"Channel 'inApp' is not supported for event 'follow'",
/// "code":"bad_request"}</c>, so the channel set must be honored, not assumed.
/// It <i>merges</i> the channels it is given, so a PATCH may send one channel
/// and leave the others alone.
/// </para>
/// </remarks>
public sealed class NotificationPreference
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }

    /// <summary>
    /// The channels this event supports, and whether each is enabled. Only keys
    /// actually present are supported — see the remarks.
    /// </summary>
    [JsonConverter(typeof(NotificationChannelsConverter))]
    public NotificationChannels Channels { get; init; } = new();
}

/// <summary>
/// An event's delivery channels, as a set rather than a fixed record — the app
/// must be able to tell "supported and off" from "not supported at all".
/// </summary>
/// <remarks>
/// The converter is declared on the type as well as on the property so that
/// serializing a <see cref="NotificationChannels"/> directly round-trips as the
/// channel object. Without it, reflection emits the public helpers
/// (<c>supportedChannels</c>, <c>values</c>) instead.
/// </remarks>
[JsonConverter(typeof(NotificationChannelsConverter))]
public sealed class NotificationChannels
{
    private readonly Dictionary<string, bool> _values = new(StringComparer.Ordinal);

    public const string Push = "push";
    public const string InApp = "inApp";
    public const string Email = "email";

    /// <summary>Canonical display order, so rows line up across events.</summary>
    private static readonly string[] Order = [InApp, Push, Email];

    public void Set(string channel, bool enabled) => _values[channel] = enabled;

    /// <summary>True when the event supports this channel at all.</summary>
    public bool Supports(string channel) => _values.ContainsKey(channel);

    /// <summary>Whether the channel is enabled. False when unsupported.</summary>
    public bool IsEnabled(string channel) => _values.TryGetValue(channel, out var v) && v;

    /// <summary>Supported channels in canonical order (unknown ones appended).</summary>
    public IReadOnlyList<string> SupportedChannels =>
        [.. _values.Keys.OrderBy(k => Array.IndexOf(Order, k) is var i && i >= 0 ? i : int.MaxValue)];

    public IReadOnlyDictionary<string, bool> Values => _values;

    /// <summary>Human label for a channel key.</summary>
    public static string LabelFor(string channel) => channel switch
    {
        Push => "Push",
        InApp => "In-app",
        Email => "Email",
        _ => channel,
    };
}

/// <summary>
/// Reads the server's <c>channels</c> object into a set, preserving exactly
/// which keys were present. A fixed POCO would erase that distinction.
/// </summary>
public sealed class NotificationChannelsConverter : JsonConverter<NotificationChannels>
{
    public override NotificationChannels Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var channels = new NotificationChannels();
        if (reader.TokenType == JsonTokenType.Null) return channels;
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return channels;
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            var name = reader.GetString()!;
            reader.Read();
            if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)
                channels.Set(name, reader.TokenType == JsonTokenType.True);
            else
                reader.Skip();
        }

        return channels;
    }

    public override void Write(Utf8JsonWriter writer, NotificationChannels value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (k, v) in value.Values) writer.WriteBoolean(k, v);
        writer.WriteEndObject();
    }
}

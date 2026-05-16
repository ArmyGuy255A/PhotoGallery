using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoGallery.Serialization;

/// <summary>
/// Forces every <see cref="DateTime"/> property in API responses to be
/// serialized as ISO 8601 in UTC with an explicit <c>Z</c> suffix
/// (e.g. <c>2026-05-16T19:55:00.1234567Z</c>).
///
/// Why this exists:
///   - System.Text.Json's default DateTime writer emits no offset when
///     <see cref="DateTime.Kind"/> is <c>Unspecified</c> — and that's
///     exactly what EF Core hands back for every DateTime column.
///   - Without a timezone marker, the browser's
///     <c>new Date("2026-05-16T19:55:00")</c> interprets the string as
///     LOCAL time, which makes timestamps appear shifted by the user's
///     UTC offset. Reported by the user as "dates aren't in browser-local
///     time".
///
/// Forcing every emitted value to UTC + <c>Z</c> means the Angular
/// <c>| date:'short'</c> pipe (which is locale-aware) renders them in the
/// visitor's local timezone correctly.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString();
        if (string.IsNullOrEmpty(raw)) return default;
        return DateTime.Parse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        DateTime utc = value.Kind switch
        {
            DateTimeKind.Utc         => value,
            DateTimeKind.Local       => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _                        => value
        };
        writer.WriteStringValue(utc.ToString("O", CultureInfo.InvariantCulture));
    }
}

/// <summary>Nullable counterpart to <see cref="UtcDateTimeJsonConverter"/>.</summary>
public sealed class NullableUtcDateTimeJsonConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var raw = reader.GetString();
        if (string.IsNullOrEmpty(raw)) return null;
        return DateTime.Parse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value == null) { writer.WriteNullValue(); return; }
        DateTime utc = value.Value.Kind switch
        {
            DateTimeKind.Utc         => value.Value,
            DateTimeKind.Local       => value.Value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
            _                        => value.Value
        };
        writer.WriteStringValue(utc.ToString("O", CultureInfo.InvariantCulture));
    }
}

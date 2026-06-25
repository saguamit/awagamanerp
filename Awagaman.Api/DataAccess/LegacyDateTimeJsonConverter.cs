using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Awagaman.Api.DataAccess;

internal sealed class LegacyDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return ReadDateTime(ref reader);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
    }

    private static DateTime ReadDateTime(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return default;
            }

            if (TryParseLegacyJavaScriptDate(text, out var legacy))
            {
                return legacy;
            }

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var iso))
            {
                return iso;
            }

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            {
                return parsed;
            }
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var unixMs))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime;
        }

        throw new JsonException($"Unable to parse DateTime value from token {reader.TokenType}.");
    }

    private static bool TryParseLegacyJavaScriptDate(string? text, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (!text.StartsWith("/Date(", StringComparison.OrdinalIgnoreCase) || !text.EndsWith(")/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var inner = text.Substring(6, text.Length - 8);
        var signIndex = inner.IndexOfAny(new[] { '+', '-' }, 1);
        if (signIndex > 0)
        {
            inner = inner.Substring(0, signIndex);
        }

        if (!long.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
        {
            return false;
        }

        value = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime;
        return true;
    }
}

internal sealed class LegacyNullableDateTimeJsonConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var converter = new LegacyDateTimeJsonConverter();
        return converter.Read(ref reader, typeof(DateTime), options);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(value.Value.ToString("O", CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

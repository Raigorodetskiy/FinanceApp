using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinanceApp.API.Infrastructure;

/// <summary>
/// JSON converter that always serializes <see cref="DateTime"/> values with a
/// trailing <c>Z</c> (UTC designator).
///
/// MySQL <c>datetime</c> columns store UTC clock values but EF Core materialises
/// them with <see cref="DateTimeKind.Unspecified"/>. Without this converter,
/// <c>System.Text.Json</c> omits the <c>Z</c> suffix for unspecified datetimes,
/// which causes JavaScript <c>Date.parse()</c> to treat the string as local time.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetDateTime();
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        writer.WriteStringValue(utc);
    }
}

/// <summary>
/// Nullable counterpart of <see cref="UtcDateTimeJsonConverter"/>.
/// </summary>
public sealed class UtcNullableDateTimeJsonConverter : JsonConverter<DateTime?>
{
    private static readonly UtcDateTimeJsonConverter Inner = new();

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        return Inner.Read(ref reader, typeof(DateTime), options);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }
        Inner.Write(writer, value.Value, options);
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Catga.Serialization;

/// <summary>
/// Custom JSON converter for Exception types to avoid AOT trimming warnings.
/// Avoids accessing Exception.TargetSite which requires reflection.
/// </summary>
[UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.", Justification = "<Pending>")]
public class CatgaExceptionJsonConverter : JsonConverter<Exception>
{
    public override Exception? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // We don't support deserializing exceptions
        throw new NotSupportedException("Deserializing exceptions is not supported.");
    }

    public override void Write(Utf8JsonWriter writer, Exception value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteString("message", value.Message);
        writer.WriteString("stackTrace", value.StackTrace);
        writer.WriteString("type", value.GetType().FullName);
        writer.WriteString("source", value.Source);

        // Don't access TargetSite - it requires reflection
        // writer.WriteString("targetSite", value.TargetSite?.ToString());

        if (value.InnerException != null)
        {
            writer.WritePropertyName("innerException");
            Write(writer, value.InnerException, options);
        }

        if (value.Data.Count > 0)
        {
            writer.WritePropertyName("data");
            writer.WriteStartObject();
            foreach (var key in value.Data.Keys)
            {
                writer.WritePropertyName(key.ToString() ?? "null");
                JsonSerializer.Serialize(writer, value.Data[key], options);
            }
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }
}


using System.Text.Json;
using System.Text.Json.Serialization;

namespace LineLanguageLearn.Learning.Infrastructure;

// Keep keyed lookups internal; persist the typed entry arrays declared by the Zod models.
// Legacy object-shaped documents remain readable and are upgraded on their next write.
public sealed class DictionaryEntriesConverter<T> : JsonConverter<Dictionary<string, T>>
{
    public override bool HandleNull => true;

    public override Dictionary<string, T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject())
                Add(property.Name, property.Value);
        }
        else if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object ||
                    !entry.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String ||
                    !entry.TryGetProperty("value", out var value))
                    throw new JsonException("Dictionary entries must have a string key and a value.");
                Add(key.GetString()!, value);
            }
        }
        else
        {
            throw new JsonException("Expected dictionary entry array or legacy object.");
        }
        return result;

        void Add(string key, JsonElement value)
        {
            var item = value.Deserialize<T>(options);
            if (item is null || !result.TryAdd(key, item))
                throw new JsonException($"Null value or duplicate dictionary key: {key}.");
        }
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var entry in value.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("key", entry.Key);
            writer.WritePropertyName("value");
            JsonSerializer.Serialize(writer, entry.Value, options);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}

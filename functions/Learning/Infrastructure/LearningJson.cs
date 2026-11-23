using System.Text.Json;
using System.Text.Json.Serialization;

namespace LineLanguageLearn.Learning.Infrastructure;

public static class LearningJson
{
    // Mirrors the Zod contracts: camelCase properties, kebab-case enum values, nulls written explicitly
    // (contracts use nullable-but-required fields).
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException($"Could not deserialize {typeof(T).Name}.");

    public static T Deserialize<T>(JsonElement json) =>
        json.Deserialize<T>(Options) ?? throw new JsonException($"Could not deserialize {typeof(T).Name}.");

    public static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, Options);

    public static T Clone<T>(T value) => Deserialize<T>(Serialize(value));
}

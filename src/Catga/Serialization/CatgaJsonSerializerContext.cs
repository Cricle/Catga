using System.Text.Json;
using System.Text.Json.Serialization;
using Catga.Results;

namespace Catga.Serialization;

/// <summary>
/// JSON Source Generator Context - 100% AOT Compatible
/// Provides compile-time source generation support for all Catga message types
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Default)]
// Basic types
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(List<string>))]
// Catga core types
[JsonSerializable(typeof(CatgaResult<string>))]
[JsonSerializable(typeof(CatgaResult<int>))]
[JsonSerializable(typeof(CatgaResult<bool>))]
[JsonSerializable(typeof(ResultMetadata))]
public partial class CatgaJsonSerializerContext : JsonSerializerContext
{
}

/// <summary>
/// Extension methods: Configure Catga source generation context for JsonSerializerOptions
/// </summary>
public static class CatgaJsonSerializerContextExtensions
{
    /// <summary>
    /// Add Catga JSON Source Generator Context (AOT compatible)
    /// </summary>
    public static JsonSerializerOptions UseCatgaContext(this JsonSerializerOptions options)
    {
        options.TypeInfoResolver = CatgaJsonSerializerContext.Default;
        return options;
    }

    /// <summary>
    /// Create default AOT-compatible JSON options
    /// </summary>
    public static JsonSerializerOptions CreateCatgaOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = CatgaJsonSerializerContext.Default
        };
        return options;
    }
}



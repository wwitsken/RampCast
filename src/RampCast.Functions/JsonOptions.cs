using System.Text.Json;

namespace RampCast.Functions;

/// <summary>
/// Shared System.Text.Json options. Web defaults give camelCase property naming
/// (matching blob-input-schema.json / output-plan-schema.json) and
/// case-insensitive reads.
/// </summary>
public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web);
}

using System.Text.Json;
using Json.Schema;

namespace RampCast.Functions.Services;

/// <summary>
/// Validates the aggregated timesheet input against blob-input-schema.json
/// (draft 2020-12) before it is sent to the LLM — so the schema-level rules
/// (e.g. tasks non-empty ⇒ phase.weeklyHours empty) are enforced rather than
/// trusted to the aggregation code.
/// </summary>
public sealed class SchemaValidator
{
    // JsonSchema.Net registers a parsed schema in a process-wide registry keyed
    // by its $id, and throws if the same $id is registered twice — so loading
    // the file in the instance constructor is only safe if exactly one
    // SchemaValidator is ever constructed per process. That held by accident
    // (Program.cs registers this as a singleton) but isn't something callers
    // should have to know; load once per process via a static field instead, so
    // constructing more than one instance — including from tests — is safe.
    private static readonly JsonSchema BlobInputSchema = LoadSchema();

    private static JsonSchema LoadSchema()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Schemas", "blob-input-schema.json");
        return JsonSchema.FromFile(path);
    }

    /// <summary>Throws <see cref="InvalidOperationException"/> if the aggregated
    /// input does not conform to blob-input-schema.json.</summary>
    public void ValidateBlobInput(JsonElement input)
    {
        var results = BlobInputSchema.Evaluate(input, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
        });

        if (results.IsValid)
            return;

        var errors = (results.Details ?? [])
            .Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))
            .Distinct();

        throw new InvalidOperationException(
            "Aggregated input failed blob-input-schema validation: " + string.Join("; ", errors));
    }
}

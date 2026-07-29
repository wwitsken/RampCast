using Microsoft.Extensions.Options;

namespace RampCast.Functions;

public sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        var hasConnectionString = !string.IsNullOrEmpty(options.ConnectionString);
        var uriCount = new[] { options.BlobServiceUri, options.QueueServiceUri, options.TableServiceUri }
            .Count(uri => uri is not null);

        if (hasConnectionString && uriCount == 0)
            return ValidateOptionsResult.Success;

        if (!hasConnectionString && uriCount == 3)
            return ValidateOptionsResult.Success;

        return ValidateOptionsResult.Fail(
            $"Invalid '{StorageOptions.SectionName}' configuration: set Storage:ConnectionString alone " +
            "(local dev / Azurite), or set Storage:BlobServiceUri, Storage:QueueServiceUri, and " +
            "Storage:TableServiceUri together (managed identity) — not both, and not a partial set of URIs.");
    }
}

namespace RampCast.Functions;

/// <summary>
/// Backs the "Storage" config section — the app's own blob/queue/table access.
/// Deliberately separate from AzureWebJobsStorage, which stays reserved for the
/// Functions host's own bookkeeping (locks, deployment package, etc.) on its own
/// storage account. Bind either ConnectionString alone (local dev via Azurite),
/// or all three *ServiceUri properties together (managed identity in Azure) —
/// see StorageOptionsValidator for the enforced shape.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string? ConnectionString { get; set; }
    public Uri? BlobServiceUri { get; set; }
    public Uri? QueueServiceUri { get; set; }
    public Uri? TableServiceUri { get; set; }
}

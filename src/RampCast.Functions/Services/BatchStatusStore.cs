using System.Text.Json;
using Azure.Storage.Blobs;
using RampCast.DocGen.Models;

namespace RampCast.Functions.Services;

/// <summary>
/// Persists per-batch status (queued → processing → complete | failed) plus the
/// generated plan to a status blob keyed by batchId. Reused by GetBatchStatus.
/// </summary>
public sealed class BatchStatusStore(BlobServiceClient blobServiceClient)
{
    private const string ContainerName = "status";

    public async Task WriteAsync(string batchId, string status, StaffingPlan? result, CancellationToken ct = default)
    {
        var container = blobServiceClient.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var blob = container.GetBlobClient(BlobName(batchId));
        var payload = new BatchStatusResponse(batchId, status, result);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions.Default);
        await blob.UploadAsync(BinaryData.FromBytes(bytes), overwrite: true, cancellationToken: ct);
    }

    public async Task<BatchStatusResponse?> ReadAsync(string batchId, CancellationToken ct = default)
    {
        var blob = blobServiceClient
            .GetBlobContainerClient(ContainerName)
            .GetBlobClient(BlobName(batchId));

        if (!await blob.ExistsAsync(ct))
            return null;

        var download = await blob.DownloadContentAsync(ct);
        return download.Value.Content.ToObjectFromJson<BatchStatusResponse>(JsonOptions.Default);
    }

    private static string BlobName(string batchId) => $"{batchId}.json";
}

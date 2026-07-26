using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace RampCast.Functions.Services;

/// <summary>
/// Stores and reads the generated staffing-plan .xlsx, one blob per batch.
/// Written by GenerateStaffingPlan, read by DownloadPlan.
/// </summary>
public sealed class PlanDocumentStore(BlobServiceClient blobServiceClient)
{
    private const string ContainerName = "plans";

    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task WriteAsync(string batchId, byte[] content, CancellationToken ct = default)
    {
        var container = blobServiceClient.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var blob = container.GetBlobClient(BlobName(batchId));
        await blob.UploadAsync(
            BinaryData.FromBytes(content),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = ContentType } },
            ct);
    }

    public async Task<byte[]?> ReadAsync(string batchId, CancellationToken ct = default)
    {
        var blob = blobServiceClient
            .GetBlobContainerClient(ContainerName)
            .GetBlobClient(BlobName(batchId));

        if (!await blob.ExistsAsync(ct))
            return null;

        var download = await blob.DownloadContentAsync(ct);
        return download.Value.Content.ToArray();
    }

    public static string FileName(string batchId) => $"{batchId}-staffing-plan.xlsx";

    private static string BlobName(string batchId) => $"{batchId}.xlsx";
}

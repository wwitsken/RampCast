using System.Security.Cryptography;
using System.Text.Json;
using Azure.Storage.Blobs;
using CsvHelper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using RampCast.Functions.Services;

namespace RampCast.Functions;

public class UploadFile(
    ILogger<UploadFile> logger,
    BlobServiceClient blobServiceClient,
    SchemaValidator schemaValidator,
    AccessTokenService accessTokens)
{
    private readonly ILogger<UploadFile> _logger = logger;
    private readonly BlobServiceClient _blobServiceClient = blobServiceClient;
    private readonly SchemaValidator _schemaValidator = schemaValidator;
    private readonly AccessTokenService _accessTokens = accessTokens;

    [Function("UploadFile")]
    public async Task<Results<Ok<UploadResponse>, BadRequest<string>, ContentHttpResult>> Upload(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "upload/{batchId?}")] HttpRequest req,
        string? batchId)
    {
        var check = await _accessTokens.CheckAsync(req, AccessGrant.Upload, req.HttpContext.RequestAborted);
        if (!check.IsAllowed)
            return check.Denial!.ToResult();

        if (req.ContentLength is null or 0)
            return TypedResults.BadRequest("No file content provided");

        using var buffer = new MemoryStream();
        await req.Body.CopyToAsync(buffer);
        buffer.Position = 0;

        // Validate the file is a well-formed timesheet CSV (docs/csv-input-schema.md)
        // that aggregates into a shape passing blob-input-schema.json, before it's
        // ever written to blob storage. This mirrors the checks GenerateStaffingPlan
        // runs later, so bad files are rejected at upload time, not analysis time.
        // The original CSV bytes are still what gets stored: GenerateStaffingPlan
        // combines the rows from every file in a batch before aggregating once, so
        // per-file conversion to the aggregated JSON shape would break multi-file
        // batches.
        try
        {
            using var reader = new StreamReader(buffer, leaveOpen: true);
            var rows = TimesheetAggregator.ParseCsv(reader);
            var input = TimesheetAggregator.Aggregate(rows);
            var inputElement = JsonSerializer.SerializeToElement(input, JsonOptions.Default);
            _schemaValidator.ValidateBlobInput(inputElement);
        }
        catch (CsvHelperException ex)
        {
            _logger.LogWarning(ex, "Rejected upload for batch {BatchId}: malformed CSV.", batchId);
            return TypedResults.BadRequest($"File is not a valid timesheet CSV: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Rejected upload for batch {BatchId}: {Reason}", batchId, ex.Message);
            return TypedResults.BadRequest(ex.Message);
        }

        buffer.Position = 0;

        batchId ??= Guid.NewGuid().ToString();

        // Content-hash the name so re-uploading an identical file overwrites
        // itself instead of silently doubling every hour in the batch — a real
        // risk now that a batch is meant to hold a whole set of comparable
        // projects, not just one, so accidental double-adds are more likely.
        var fileName = Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
        var containerClient = _blobServiceClient.GetBlobContainerClient("uploads");

        await containerClient.CreateIfNotExistsAsync();

        var blobClient = containerClient.GetBlobClient($"{batchId}/{fileName}");
        await blobClient.UploadAsync(buffer, overwrite: true);

        await _accessTokens.CommitAsync(check, req.HttpContext.RequestAborted);

        return TypedResults.Ok(new UploadResponse(batchId!, fileName));
    }
}

public record UploadResponse(string BatchId, string FileName);
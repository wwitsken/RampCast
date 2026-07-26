using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using RampCast.Functions.Models;
using RampCast.Functions.Services;

namespace RampCast.Functions;

public class AnalyzeBatch(ILogger<AnalyzeBatch> logger,
                          BlobServiceClient blobServiceClient,
                          QueueServiceClient queueServiceClient,
                          BatchStatusStore statusStore,
                          AccessTokenService accessTokens)
{
    private const int MaxGuidanceLength = 4000;

    private readonly ILogger<AnalyzeBatch> _logger = logger;
    private readonly BlobServiceClient _blobServiceClient = blobServiceClient;
    private readonly QueueServiceClient _queueServiceClient = queueServiceClient;
    private readonly BatchStatusStore _statusStore = statusStore;
    private readonly AccessTokenService _accessTokens = accessTokens;

    [Function("AnalyzeBatch")]
    public async Task<Results<Ok<AnalyzeResponse>, BadRequest<string>, ContentHttpResult>> Analyze(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "analyze/{batchId}")] HttpRequest req,
        string batchId)
    {
        var check = await _accessTokens.CheckAsync(req, AccessGrant.Analysis, req.HttpContext.RequestAborted);
        if (!check.IsAllowed)
            return check.Denial!.ToResult();

        string? guidance = null;
        if (req.ContentLength is > 0)
        {
            using var reader = new StreamReader(req.Body);
            var body = (await reader.ReadToEndAsync()).Trim();
            if (body.Length > 0)
            {
                AnalyzeRequest? parsed;
                try
                {
                    parsed = JsonSerializer.Deserialize<AnalyzeRequest>(body, JsonOptions.Default);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Rejected analyze for batch {BatchId}: malformed request body.", batchId);
                    return TypedResults.BadRequest("Request body must be JSON of the form {\"guidance\": \"...\"}.");
                }

                guidance = string.IsNullOrWhiteSpace(parsed?.Guidance) ? null : parsed.Guidance.Trim();
                if (guidance is { Length: > MaxGuidanceLength })
                    return TypedResults.BadRequest($"Guidance must be {MaxGuidanceLength} characters or fewer.");
            }
        }

        var containerClient = _blobServiceClient.GetBlobContainerClient("uploads");

        var fileCount = 0;
        await foreach (var _ in containerClient.GetBlobsAsync(
                           BlobTraits.None, BlobStates.None, $"{batchId}/", default))
        {
            fileCount++;
        }

        // Written before enqueueing so GetBatchStatus never 404s for a batch that
        // has already started analysis — otherwise there's a window between this
        // call returning and the worker's first status write.
        await _statusStore.WriteAsync(batchId, "queued", result: null, req.HttpContext.RequestAborted);

        var queueClient = _queueServiceClient.GetQueueClient("batch-analysis");
        await queueClient.CreateIfNotExistsAsync();

        // Storage Queue messages must be base64-encoded by default.
        var envelope = JsonSerializer.Serialize(new BatchAnalysisMessage(batchId, guidance), JsonOptions.Default);
        var messageBody = Convert.ToBase64String(Encoding.UTF8.GetBytes(envelope));
        await queueClient.SendMessageAsync(messageBody);

        await _accessTokens.CommitAsync(check, req.HttpContext.RequestAborted);

        return TypedResults.Ok(new AnalyzeResponse(batchId, "Batch analysis started", fileCount));
    }
}

public record AnalyzeRequest(string? Guidance);

public record AnalyzeResponse(string BatchId, string Result, int FileCount);
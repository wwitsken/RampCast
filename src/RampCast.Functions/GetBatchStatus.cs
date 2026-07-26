using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using RampCast.DocGen.Models;
using RampCast.Functions.Services;

namespace RampCast.Functions;

public class GetBatchStatus(ILogger<GetBatchStatus> logger, BatchStatusStore statusStore, AccessTokenService accessTokens)
{
    private readonly ILogger<GetBatchStatus> _logger = logger;
    private readonly BatchStatusStore _statusStore = statusStore;
    private readonly AccessTokenService _accessTokens = accessTokens;

    [Function("GetBatchStatus")]
    public async Task<Results<Ok<BatchStatusResponse>, NotFound, ContentHttpResult>> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "status/{batchId}")] HttpRequest req,
        string batchId)
    {
        var check = await _accessTokens.CheckAsync(req, grant: null, req.HttpContext.RequestAborted);
        if (!check.IsAllowed)
            return check.Denial!.ToResult();

        _logger.LogInformation("Fetching status for batch {BatchId}.", batchId);

        var status = await _statusStore.ReadAsync(batchId, req.HttpContext.RequestAborted);

        if (status is null)
            return TypedResults.NotFound();

        // The .xlsx is uploaded before the "complete" status is written, so a
        // complete status guarantees the plan blob exists. Surface a download link
        // to the DownloadPlan endpoint, built from the incoming request.
        if (status.Status == "complete")
            status = status with { DownloadUrl = $"{req.Scheme}://{req.Host}/api/plans/{batchId}" };

        return TypedResults.Ok(status);
    }
}

// Status values: "queued" -> "processing" -> "complete" | "failed"
public record BatchStatusResponse(string BatchId, string Status, StaffingPlan? Result, string? DownloadUrl = null);

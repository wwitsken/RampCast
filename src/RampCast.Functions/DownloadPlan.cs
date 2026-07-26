using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using RampCast.Functions.Services;

namespace RampCast.Functions;

public class DownloadPlan(ILogger<DownloadPlan> logger, PlanDocumentStore planStore, AccessTokenService accessTokens)
{
    private readonly ILogger<DownloadPlan> _logger = logger;
    private readonly PlanDocumentStore _planStore = planStore;
    private readonly AccessTokenService _accessTokens = accessTokens;

    [Function("DownloadPlan")]
    public async Task<Results<FileContentHttpResult, NotFound, ContentHttpResult>> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "plans/{batchId}")] HttpRequest req,
        string batchId)
    {
        var check = await _accessTokens.CheckAsync(req, grant: null, req.HttpContext.RequestAborted);
        if (!check.IsAllowed)
            return check.Denial!.ToResult();

        _logger.LogInformation("Downloading staffing plan for batch {BatchId}.", batchId);

        var content = await _planStore.ReadAsync(batchId, req.HttpContext.RequestAborted);

        return content is null
            ? TypedResults.NotFound()
            : TypedResults.File(content, PlanDocumentStore.ContentType, PlanDocumentStore.FileName(batchId));
    }
}

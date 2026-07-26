using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using RampCast.Functions.Services;

namespace RampCast.Functions;

public class GetTokenUsage(ILogger<GetTokenUsage> logger, AccessTokenService accessTokens)
{
    private readonly ILogger<GetTokenUsage> _logger = logger;
    private readonly AccessTokenService _accessTokens = accessTokens;

    [Function("GetTokenUsage")]
    public async Task<Results<Ok<TokenUsageResponse>, ContentHttpResult>> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tokens/usage")] HttpRequest req)
    {
        // grant: null validates the token without requiring remaining quota, so
        // an exhausted-but-otherwise-valid token still reports its (zero) usage
        // instead of bouncing the frontend back to the token-entry gate.
        var check = await _accessTokens.CheckAsync(req, grant: null, req.HttpContext.RequestAborted);
        if (!check.IsAllowed)
            return check.Denial!.ToResult();

        var entity = check.Entity!;
        _logger.LogInformation("Reporting token usage for access token {Token}.", check.Token);

        return TypedResults.Ok(new TokenUsageResponse(
            entity.UploadsRemaining, entity.UploadGrants,
            entity.AnalysesRemaining, entity.AnalysisGrants,
            entity.ExpiresAt));
    }
}

public record TokenUsageResponse(
    int UploadsRemaining, int UploadGrants,
    int AnalysesRemaining, int AnalysisGrants,
    DateTimeOffset? ExpiresAt);

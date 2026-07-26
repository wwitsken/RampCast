using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using RampCast.Functions.Services;

namespace RampCast.Functions;

public class MintAccessToken(ILogger<MintAccessToken> logger, AuthTokenStore store)
{
    private const int DefaultUploadGrants = 25;
    private const int DefaultAnalysisGrants = 5;
    private const int MaxGrants = 1000;
    private const int MaxExpiresInDays = 365;

    private readonly ILogger<MintAccessToken> _logger = logger;
    private readonly AuthTokenStore _store = store;

    // Admin-level: callable with the Function App's master key, so a token can
    // be minted with a bare curl POST and no request body.
    [Function("MintAccessToken")]
    public async Task<Results<Ok<MintTokenResponse>, BadRequest<string>>> Mint(
        [HttpTrigger(AuthorizationLevel.Admin, "post", Route = "tokens")] HttpRequest req)
    {
        MintTokenRequest? parsed = null;
        if (req.ContentLength is > 0)
        {
            using var reader = new StreamReader(req.Body);
            var body = (await reader.ReadToEndAsync()).Trim();
            if (body.Length > 0)
            {
                try
                {
                    parsed = JsonSerializer.Deserialize<MintTokenRequest>(body, JsonOptions.Default);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Rejected mint request: malformed request body.");
                    return TypedResults.BadRequest(
                        "Request body must be JSON of the form {\"uploadGrants\": 25, \"analysisGrants\": 5, \"expiresInDays\": null}.");
                }
            }
        }

        var (uploadGrants, analysisGrants, expiresAt, error) = ResolveGrants(parsed);
        if (error is not null)
            return TypedResults.BadRequest(error);

        var entity = await _store.CreateAsync(uploadGrants, analysisGrants, expiresAt, req.HttpContext.RequestAborted);

        _logger.LogInformation(
            "Minted access token with {UploadGrants} upload / {AnalysisGrants} analysis grants.",
            uploadGrants, analysisGrants);

        return TypedResults.Ok(new MintTokenResponse(entity.RowKey, uploadGrants, analysisGrants, expiresAt));
    }

    /// <summary>
    /// Pure defaulting/validation, split out so it's unit-testable without a
    /// TableServiceClient: null/absent fields fall back to 25 uploads / 5
    /// analyses / no expiration; explicit values are range-checked.
    /// </summary>
    internal static (int UploadGrants, int AnalysisGrants, DateTimeOffset? ExpiresAt, string? Error) ResolveGrants(
        MintTokenRequest? request)
    {
        var uploadGrants = request?.UploadGrants ?? DefaultUploadGrants;
        var analysisGrants = request?.AnalysisGrants ?? DefaultAnalysisGrants;

        if (uploadGrants is < 1 or > MaxGrants)
            return (0, 0, null, $"uploadGrants must be between 1 and {MaxGrants}.");
        if (analysisGrants is < 1 or > MaxGrants)
            return (0, 0, null, $"analysisGrants must be between 1 and {MaxGrants}.");

        DateTimeOffset? expiresAt = null;
        if (request?.ExpiresInDays is { } days)
        {
            if (days is < 1 or > MaxExpiresInDays)
                return (0, 0, null, $"expiresInDays must be between 1 and {MaxExpiresInDays}.");
            expiresAt = DateTimeOffset.UtcNow.AddDays(days);
        }

        return (uploadGrants, analysisGrants, expiresAt, null);
    }
}

public record MintTokenRequest(int? UploadGrants, int? AnalysisGrants, int? ExpiresInDays);

public record MintTokenResponse(string Token, int UploadGrants, int AnalysisGrants, DateTimeOffset? ExpiresAt);

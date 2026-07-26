using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;

namespace RampCast.Functions.Services;

/// <summary>
/// The gate every HTTP function calls through: validates the X-RampCast-Token
/// header against AuthTokenStore and, for grant-consuming endpoints, commits
/// the decrement only after the caller's work has actually succeeded. Split
/// into CheckAsync (up front) / CommitAsync (after success) so a request that
/// fails its own validation (e.g. a malformed CSV) never burns quota.
/// </summary>
public sealed class AccessTokenService(AuthTokenStore store, ILogger<AccessTokenService> logger)
{
    public const string HeaderName = "X-RampCast-Token";

    public async Task<AccessCheck> CheckAsync(HttpRequest req, AccessGrant? grant, CancellationToken ct = default)
    {
        var token = ReadToken(req);
        if (token is null)
            return new AccessCheck(null, null, grant, new AccessDenial(401, "Missing or malformed access token."));

        var entity = await store.FindAsync(token, ct);
        if (entity is null)
            return new AccessCheck(token, null, grant, new AccessDenial(401, "Access token not recognized."));

        if (entity.ExpiresAt is { } expiresAt && expiresAt < DateTimeOffset.UtcNow)
            return new AccessCheck(token, entity, grant, new AccessDenial(401, "Access token has expired."));

        if (grant is { } g)
        {
            var remaining = g == AccessGrant.Upload ? entity.UploadsRemaining : entity.AnalysesRemaining;
            if (remaining <= 0)
            {
                var kind = g == AccessGrant.Upload ? "uploads" : "analyses";
                return new AccessCheck(token, entity, grant,
                    new AccessDenial(403, $"This access token has no {kind} remaining."));
            }
        }

        return new AccessCheck(token, entity, grant, null);
    }

    public async Task CommitAsync(AccessCheck check, CancellationToken ct = default)
    {
        if (check.Token is null || check.Grant is not { } grant)
            return;

        var result = await store.TryConsumeAsync(check.Token, grant, ct);
        if (result is null)
        {
            // The check passed but the commit lost a race (or the token vanished
            // between check and commit). The caller's work already succeeded, so
            // don't fail the request over an accounting miss — just log it.
            logger.LogWarning(
                "Failed to commit {Grant} usage for access token {Token}: token missing or exhausted at commit time.",
                grant, check.Token);
        }
    }

    internal static string? ReadToken(HttpRequest req) =>
        Normalize(req.Headers[HeaderName].FirstOrDefault());

    /// <summary>
    /// Accepts any Guid-parseable form (dashed, braced, uppercase) and
    /// normalizes to the lowercase 32-digit "N" format used as the RowKey, so
    /// input variance never causes a lookup miss and every stored token is a
    /// safe RowKey (no slashes or other reserved characters).
    /// </summary>
    internal static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return Guid.TryParse(raw.Trim(), out var guid) ? guid.ToString("N") : null;
    }
}

public readonly record struct AccessCheck(
    string? Token, AuthTokenEntity? Entity, AccessGrant? Grant, AccessDenial? Denial)
{
    public bool IsAllowed => Denial is null;
}

public sealed record AccessDenial(int StatusCode, string Message);

public static class AccessDenialExtensions
{
    public static ContentHttpResult ToResult(this AccessDenial denial) =>
        TypedResults.Text(denial.Message, "text/plain", statusCode: denial.StatusCode);
}

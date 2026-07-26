using System.Runtime.Serialization;
using Azure;
using Azure.Data.Tables;

namespace RampCast.Functions.Services;

/// <summary>
/// Persists access-token quota rows in Azure Table Storage. Tokens gate the
/// upload/analyze endpoints: each row tracks how many uploads/analyses have
/// been granted vs. used. Reused by AccessTokenService, which layers the
/// check/commit workflow on top of the raw CRUD here.
/// </summary>
public sealed class AuthTokenStore(TableServiceClient tableServiceClient, string tableName = "AccessTokens")
{
    private const int MaxConsumeAttempts = 3;

    private TableClient Table => tableServiceClient.GetTableClient(tableName);

    public async Task<AuthTokenEntity> CreateAsync(
        int uploadGrants, int analysisGrants, DateTimeOffset? expiresAt, CancellationToken ct = default)
    {
        var table = Table;
        await table.CreateIfNotExistsAsync(ct);

        var entity = new AuthTokenEntity
        {
            RowKey = Guid.NewGuid().ToString("N"),
            UploadGrants = uploadGrants,
            AnalysisGrants = analysisGrants,
            ExpiresAt = expiresAt,
        };

        await table.AddEntityAsync(entity, ct);
        return entity;
    }

    public async Task<AuthTokenEntity?> FindAsync(string token, CancellationToken ct = default)
    {
        try
        {
            var response = await Table.GetEntityAsync<AuthTokenEntity>(
                AuthTokenEntity.DefaultPartitionKey, token, cancellationToken: ct);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Covers both "table doesn't exist yet" and "no row with this key" —
            // callers only care that the token isn't valid, not why.
            return null;
        }
    }

    /// <summary>
    /// Atomically increments the used-count for the given grant kind, retrying
    /// on ETag conflicts from concurrent requests against the same token.
    /// Returns null if the token doesn't exist or has no remaining quota for
    /// that grant kind at the time of the attempt — callers must not have
    /// already performed the work they'd be charging for in that case.
    /// </summary>
    public async Task<AuthTokenEntity?> TryConsumeAsync(
        string token, AccessGrant grant, CancellationToken ct = default)
    {
        var table = Table;

        for (var attempt = 0; attempt < MaxConsumeAttempts; attempt++)
        {
            AuthTokenEntity entity;
            try
            {
                var response = await table.GetEntityAsync<AuthTokenEntity>(
                    AuthTokenEntity.DefaultPartitionKey, token, cancellationToken: ct);
                entity = response.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }

            var remaining = grant == AccessGrant.Upload ? entity.UploadsRemaining : entity.AnalysesRemaining;
            if (remaining <= 0)
                return null;

            if (grant == AccessGrant.Upload)
                entity.UploadsUsed++;
            else
                entity.AnalysesUsed++;

            try
            {
                await table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);
                return entity;
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // Lost a race with another request against the same token; retry
                // against the latest ETag rather than losing the decrement.
            }
        }

        return null;
    }
}

public enum AccessGrant
{
    Upload,
    Analysis,
}

public sealed class AuthTokenEntity : ITableEntity
{
    public const string DefaultPartitionKey = "access-token";

    public string PartitionKey { get; set; } = DefaultPartitionKey;
    public string RowKey { get; set; } = string.Empty;
    public int UploadGrants { get; set; }
    public int AnalysisGrants { get; set; }
    public int UploadsUsed { get; set; }
    public int AnalysesUsed { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Azure.Data.Tables serializes every public getter by default; these are
    // derived, not stored columns, so they must be excluded explicitly.
    [IgnoreDataMember]
    public int UploadsRemaining => Math.Max(0, UploadGrants - UploadsUsed);

    [IgnoreDataMember]
    public int AnalysesRemaining => Math.Max(0, AnalysisGrants - AnalysesUsed);
}

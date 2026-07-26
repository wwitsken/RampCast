using Azure.Data.Tables;
using RampCast.Functions.Services;
using Xunit;

namespace RampCast.Functions.Tests;

/// <summary>
/// Round-trips AuthTokenStore against a real Table Storage endpoint (Azurite,
/// started locally via `npm run dev`/`azurite`). These skip cleanly rather than
/// fail when the emulator isn't reachable, since CI/dev machines won't always
/// have it running. Each test gets its own table (named per-instance from a
/// Guid) so runs never collide or need to share cleanup ordering.
/// </summary>
public class AuthTokenStoreAzuriteTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";

    private static readonly Lazy<bool> AzuriteAvailableLazy = new(ProbeAzurite);
    private static bool AzuriteAvailable => AzuriteAvailableLazy.Value;

    private readonly string _tableName = $"TestTokens{Guid.NewGuid():N}";
    private AuthTokenStore _store = null!;

    public ValueTask InitializeAsync()
    {
        _store = new AuthTokenStore(new TableServiceClient(ConnectionString), _tableName);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (!AzuriteAvailable)
            return;

        await new TableServiceClient(ConnectionString).DeleteTableAsync(_tableName);
    }

    [Fact]
    public async Task CreateThenFind_RoundTripsAllFields()
    {
        Assert.SkipUnless(AzuriteAvailable, "Azurite is not running.");

        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);
        var created = await _store.CreateAsync(25, 5, expiresAt, TestContext.Current.CancellationToken);

        var found = await _store.FindAsync(created.RowKey, TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal(created.RowKey, found.RowKey);
        Assert.Equal(25, found.UploadGrants);
        Assert.Equal(5, found.AnalysisGrants);
        Assert.Equal(0, found.UploadsUsed);
        Assert.Equal(0, found.AnalysesUsed);
        Assert.Equal(25, found.UploadsRemaining);
        Assert.Equal(5, found.AnalysesRemaining);
        Assert.Equal(expiresAt, found.ExpiresAt);
    }

    [Fact]
    public async Task TryConsume_Upload_OnlyDecrementsUploadCounter()
    {
        Assert.SkipUnless(AzuriteAvailable, "Azurite is not running.");

        var created = await _store.CreateAsync(2, 2, null, TestContext.Current.CancellationToken);

        var consumed = await _store.TryConsumeAsync(created.RowKey, AccessGrant.Upload, TestContext.Current.CancellationToken);

        Assert.NotNull(consumed);
        Assert.Equal(1, consumed.UploadsRemaining);
        Assert.Equal(2, consumed.AnalysesRemaining);
    }

    [Fact]
    public async Task TryConsume_Analysis_OnlyDecrementsAnalysisCounter()
    {
        Assert.SkipUnless(AzuriteAvailable, "Azurite is not running.");

        var created = await _store.CreateAsync(2, 2, null, TestContext.Current.CancellationToken);

        var consumed = await _store.TryConsumeAsync(created.RowKey, AccessGrant.Analysis, TestContext.Current.CancellationToken);

        Assert.NotNull(consumed);
        Assert.Equal(2, consumed.UploadsRemaining);
        Assert.Equal(1, consumed.AnalysesRemaining);
    }

    [Fact]
    public async Task TryConsume_RepeatedlyUntilExhausted_ThenReturnsNull()
    {
        Assert.SkipUnless(AzuriteAvailable, "Azurite is not running.");

        var created = await _store.CreateAsync(1, 1, null, TestContext.Current.CancellationToken);

        var first = await _store.TryConsumeAsync(created.RowKey, AccessGrant.Upload, TestContext.Current.CancellationToken);
        var second = await _store.TryConsumeAsync(created.RowKey, AccessGrant.Upload, TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.Equal(0, first.UploadsRemaining);
        Assert.Null(second);
    }

    [Fact]
    public async Task Find_UnknownToken_ReturnsNull()
    {
        Assert.SkipUnless(AzuriteAvailable, "Azurite is not running.");

        var found = await _store.FindAsync(Guid.NewGuid().ToString("N"), TestContext.Current.CancellationToken);

        Assert.Null(found);
    }

    [Fact]
    public async Task Find_TableDoesNotExistYet_ReturnsNullRatherThanThrowing()
    {
        Assert.SkipUnless(AzuriteAvailable, "Azurite is not running.");

        // No CreateAsync call in this test, so the table itself was never
        // created — FindAsync must handle a missing table the same way it
        // handles a missing row.
        var found = await _store.FindAsync(Guid.NewGuid().ToString("N"), TestContext.Current.CancellationToken);

        Assert.Null(found);
    }

    [Fact]
    public async Task TryConsume_UnknownToken_ReturnsNullRatherThanThrowing()
    {
        Assert.SkipUnless(AzuriteAvailable, "Azurite is not running.");

        var consumed = await _store.TryConsumeAsync(
            Guid.NewGuid().ToString("N"), AccessGrant.Upload, TestContext.Current.CancellationToken);

        Assert.Null(consumed);
    }

    private static bool ProbeAzurite()
    {
        try
        {
            var client = new TableServiceClient(ConnectionString);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            client.Query(cancellationToken: cts.Token).GetEnumerator().MoveNext();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

using Microsoft.AspNetCore.Http;
using RampCast.Functions.Services;
using Xunit;

namespace RampCast.Functions.Tests;

/// <summary>
/// Pure-function coverage for the access-token gate: header parsing/GUID
/// normalization in AccessTokenService, remaining-quota math on
/// AuthTokenEntity, and the mint-request defaulting/validation in
/// MintAccessToken. None of this touches Table Storage — see
/// AuthTokenStoreAzuriteTests for the store's round-trip behavior.
/// </summary>
public class AccessTokenTests
{
    [Theory]
    [InlineData("3fa85f64-5717-4562-b3fc-2c963f66afa6")]
    [InlineData("3FA85F64-5717-4562-B3FC-2C963F66AFA6")]
    [InlineData("{3fa85f64-5717-4562-b3fc-2c963f66afa6}")]
    [InlineData("3fa85f6457174562b3fc2c963f66afa6")]
    [InlineData("  3fa85f64-5717-4562-b3fc-2c963f66afa6  ")]
    public void Normalize_AnyGuidParseableForm_ReturnsLowercase32DigitFormat(string raw)
    {
        var normalized = AccessTokenService.Normalize(raw);

        Assert.Equal("3fa85f6457174562b3fc2c963f66afa6", normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    [InlineData("'; DROP TABLE AccessTokens; --")]
    public void Normalize_NonGuidInput_ReturnsNull(string? raw)
    {
        Assert.Null(AccessTokenService.Normalize(raw));
    }

    [Fact]
    public void ReadToken_HeaderPresent_ReturnsNormalizedValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[AccessTokenService.HeaderName] = "3FA85F64-5717-4562-B3FC-2C963F66AFA6";

        var token = AccessTokenService.ReadToken(context.Request);

        Assert.Equal("3fa85f6457174562b3fc2c963f66afa6", token);
    }

    [Fact]
    public void ReadToken_HeaderAbsent_ReturnsNull()
    {
        var context = new DefaultHttpContext();

        Assert.Null(AccessTokenService.ReadToken(context.Request));
    }

    [Fact]
    public void ReadToken_HeaderMalformed_ReturnsNull()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[AccessTokenService.HeaderName] = "definitely-not-a-token";

        Assert.Null(AccessTokenService.ReadToken(context.Request));
    }

    [Theory]
    [InlineData(25, 0, 25)]
    [InlineData(25, 10, 15)]
    [InlineData(25, 25, 0)]
    [InlineData(5, 9, 0)] // used exceeding grants (e.g. a raced/legacy row) clamps at 0, never negative
    public void AuthTokenEntity_UploadsRemaining_ClampsAtZero(int grants, int used, int expectedRemaining)
    {
        var entity = new AuthTokenEntity { UploadGrants = grants, UploadsUsed = used };

        Assert.Equal(expectedRemaining, entity.UploadsRemaining);
    }

    [Theory]
    [InlineData(5, 0, 5)]
    [InlineData(5, 4, 1)]
    [InlineData(5, 5, 0)]
    [InlineData(5, 7, 0)]
    public void AuthTokenEntity_AnalysesRemaining_ClampsAtZero(int grants, int used, int expectedRemaining)
    {
        var entity = new AuthTokenEntity { AnalysisGrants = grants, AnalysesUsed = used };

        Assert.Equal(expectedRemaining, entity.AnalysesRemaining);
    }

    [Fact]
    public void ResolveGrants_NullRequest_UsesDefaultUploadAndAnalysisGrants()
    {
        var (uploadGrants, analysisGrants, expiresAt, error) = MintAccessToken.ResolveGrants(null);

        Assert.Null(error);
        Assert.Equal(25, uploadGrants);
        Assert.Equal(5, analysisGrants);
        Assert.Null(expiresAt);
    }

    [Fact]
    public void ResolveGrants_EmptyRequest_UsesDefaultUploadAndAnalysisGrants()
    {
        var (uploadGrants, analysisGrants, expiresAt, error) =
            MintAccessToken.ResolveGrants(new MintTokenRequest(null, null, null));

        Assert.Null(error);
        Assert.Equal(25, uploadGrants);
        Assert.Equal(5, analysisGrants);
        Assert.Null(expiresAt);
    }

    [Fact]
    public void ResolveGrants_ExplicitValues_Honored()
    {
        var (uploadGrants, analysisGrants, expiresAt, error) =
            MintAccessToken.ResolveGrants(new MintTokenRequest(100, 10, 30));

        Assert.Null(error);
        Assert.Equal(100, uploadGrants);
        Assert.Equal(10, analysisGrants);
        Assert.NotNull(expiresAt);
        Assert.True(expiresAt > DateTimeOffset.UtcNow.AddDays(29));
        Assert.True(expiresAt < DateTimeOffset.UtcNow.AddDays(31));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void ResolveGrants_UploadGrantsOutOfRange_ReturnsError(int uploadGrants)
    {
        var (_, _, _, error) = MintAccessToken.ResolveGrants(new MintTokenRequest(uploadGrants, null, null));

        Assert.NotNull(error);
        Assert.Contains("uploadGrants", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void ResolveGrants_AnalysisGrantsOutOfRange_ReturnsError(int analysisGrants)
    {
        var (_, _, _, error) = MintAccessToken.ResolveGrants(new MintTokenRequest(null, analysisGrants, null));

        Assert.NotNull(error);
        Assert.Contains("analysisGrants", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public void ResolveGrants_ExpiresInDaysOutOfRange_ReturnsError(int expiresInDays)
    {
        var (_, _, _, error) = MintAccessToken.ResolveGrants(new MintTokenRequest(null, null, expiresInDays));

        Assert.NotNull(error);
        Assert.Contains("expiresInDays", error);
    }
}

using System.Text.Json;
using RampCast.Functions;
using RampCast.Functions.Models;
using RampCast.Functions.Services;
using Xunit;

namespace RampCast.Functions.Tests;

/// <summary>
/// Contract-level checks that don't fit the aggregator test classes: the
/// serialized shape has no leftover absolute-date keys, the docs/samples/*.json
/// fixtures stay in sync with the schema, and GenerateStaffingPlan's queue
/// message envelope parsing handles both the new and legacy message shapes.
/// </summary>
public class PipelineContractTests
{
    private static readonly string TimesheetsDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Timesheets");

    private static readonly string[] AllSampleFiles =
    [
        "sample-timesheet-cascade-elementary.csv",
        "sample-timesheet-harborview-clinic.csv",
        "sample-timesheet-meadowbrook-apartments.csv",
        "sample-timesheet-northgate-office.csv",
        "sample-timesheet-riverside.csv",
        "sample-timesheet-sunridge-library.csv",
    ];

    [Fact]
    public void AllSampleFiles_Aggregated_SerializeWithNoLeftoverAbsoluteDateKeys()
    {
        var rows = AllSampleFiles
            .SelectMany(fileName =>
            {
                using var reader = new StreamReader(Path.Combine(TimesheetsDir, fileName));
                return TimesheetAggregator.ParseCsv(reader);
            })
            .ToList();

        var input = TimesheetAggregator.Aggregate(rows);
        var json = JsonSerializer.Serialize(input, JsonOptions.Default);

        Assert.DoesNotContain("weekOf", json);
        Assert.DoesNotContain("startDate", json);
        Assert.DoesNotContain("endDate", json);
    }

    [Theory]
    [InlineData("blob-input-sample.json")]
    [InlineData("blob-input-sample-project-only.json")]
    public void DocSample_DeserializesAndValidatesAgainstSchema(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        var json = File.ReadAllText(path);

        var input = JsonSerializer.Deserialize<StaffingPlanInput>(json, JsonOptions.Default);
        Assert.NotNull(input);
        Assert.NotEmpty(input.Projects);

        using var doc = JsonDocument.Parse(json);
        new SchemaValidator().ValidateBlobInput(doc.RootElement); // throws on failure
    }

    [Fact]
    public void ParseMessage_EnvelopeWithGuidance_ReturnsBoth()
    {
        var envelope = """{"batchId":"abc-123","guidance":"Plan for a 20-week project"}""";

        var (batchId, guidance) = GenerateStaffingPlan.ParseMessage(envelope);

        Assert.Equal("abc-123", batchId);
        Assert.Equal("Plan for a 20-week project", guidance);
    }

    [Fact]
    public void ParseMessage_EnvelopeWithoutGuidance_ReturnsNullGuidance()
    {
        var envelope = """{"batchId":"abc-123","guidance":null}""";

        var (batchId, guidance) = GenerateStaffingPlan.ParseMessage(envelope);

        Assert.Equal("abc-123", batchId);
        Assert.Null(guidance);
    }

    [Fact]
    public void ParseMessage_BareLegacyBatchId_ReturnsItWithNullGuidance()
    {
        var (batchId, guidance) = GenerateStaffingPlan.ParseMessage("abc-123");

        Assert.Equal("abc-123", batchId);
        Assert.Null(guidance);
    }

    [Fact]
    public void ParseMessage_MalformedJsonLeadingBrace_FallsBackToLegacyInterpretation()
    {
        // Not valid JSON, but starts with '{' — must not throw, must fall back
        // rather than crash the queue trigger on an unparseable message.
        var (batchId, guidance) = GenerateStaffingPlan.ParseMessage("{not valid json");

        Assert.Equal("{not valid json", batchId);
        Assert.Null(guidance);
    }
}

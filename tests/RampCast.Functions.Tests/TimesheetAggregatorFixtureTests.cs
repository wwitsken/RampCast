using System.Text.Json;
using RampCast.Functions;
using RampCast.Functions.Models;
using RampCast.Functions.Services;
using Xunit;

namespace RampCast.Functions.Tests;

/// <summary>
/// Exercises TimesheetAggregator against the real sample CSVs in
/// docs/samples/timesheets/ (linked into Fixtures/Timesheets at build time) and
/// against blob-input-schema.json via SchemaValidator, so these tests fail the
/// moment a real fixture stops round-tripping through the pipeline.
/// </summary>
public class TimesheetAggregatorFixtureTests
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Timesheets");

    public static IEnumerable<object[]> SampleFiles =>
    [
        ["sample-timesheet-cascade-elementary.csv", "R26.0002.00"],
        ["sample-timesheet-harborview-clinic.csv", "R26.0015.00"],
        ["sample-timesheet-meadowbrook-apartments.csv", "R24.0089.00"],
        ["sample-timesheet-northgate-office.csv", "R25.0114.00"],
        ["sample-timesheet-riverside.csv", "R26.0001.00"],
        ["sample-timesheet-sunridge-library.csv", "R26.0007.00"],
    ];

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void EachSampleFile_ParsedAlone_AggregatesToOneValidProject(string fileName, string expectedWbsCode)
    {
        var rows = ParseFixture(fileName);

        var input = TimesheetAggregator.Aggregate(rows);

        Assert.Single(input.Projects);
        Assert.Equal(expectedWbsCode, input.Projects[0].WbsCode);
        ValidateAgainstSchema(input);
    }

    /// <summary>
    /// Load-bearing: concatenating rows from every sample file and aggregating
    /// once (exactly what GenerateStaffingPlan does for a multi-file batch) must
    /// produce the same per-project durationWeeks as aggregating each file's rows
    /// alone. If per-project week-0 anchoring were accidentally batch-wide instead
    /// of per-project, this is what would catch it.
    /// </summary>
    [Fact]
    public void AllSampleFiles_ConcatenatedRows_AggregateIntoIndependentPerProjectDurations()
    {
        var expectedDurationByCode = new Dictionary<string, int>();
        var allRows = new List<TimesheetRow>();

        foreach (object[] entry in SampleFiles)
        {
            var fileName = (string)entry[0];
            var rows = ParseFixture(fileName);
            allRows.AddRange(rows);

            var solo = TimesheetAggregator.Aggregate(rows);
            var project = Assert.Single(solo.Projects);
            expectedDurationByCode[project.WbsCode] = project.DurationWeeks;
        }

        var combined = TimesheetAggregator.Aggregate(allRows);

        Assert.Equal(expectedDurationByCode.Count, combined.Projects.Count);
        Assert.Equal(
            expectedDurationByCode.Keys.OrderBy(k => k, StringComparer.Ordinal),
            combined.Projects.Select(p => p.WbsCode));

        foreach (var project in combined.Projects)
            Assert.Equal(expectedDurationByCode[project.WbsCode], project.DurationWeeks);

        ValidateAgainstSchema(combined);
    }

    [Fact]
    public void SunridgeLibrary_HasUnphasedRowsAlongsidePhases_BothPopulated()
    {
        var rows = ParseFixture("sample-timesheet-sunridge-library.csv");

        var input = TimesheetAggregator.Aggregate(rows);
        var project = Assert.Single(input.Projects);

        // Two pursuit-debrief rows with blank wbs2, sitting alongside a later
        // Schematic Design phase — the coexistence case documented in
        // docs/csv-input-schema.md point 4.
        Assert.NotEmpty(project.WeeklyHours);
        Assert.NotEmpty(project.Phases);
        Assert.Equal(0, project.WeeklyHours.Min(w => w.WeekIndex));

        ValidateAgainstSchema(input);
    }

    [Fact]
    public void RiversideMedicalCenter_PhaseLeafAndTaskLeafBothPresent()
    {
        var rows = ParseFixture("sample-timesheet-riverside.csv");
        var input = TimesheetAggregator.Aggregate(rows);
        var project = Assert.Single(input.Projects);

        var schematicDesign = project.Phases.Single(p => p.WbsCode == "01");
        Assert.NotEmpty(schematicDesign.WeeklyHours);
        Assert.Empty(schematicDesign.Tasks);

        var constructionManagement = project.Phases.Single(p => p.WbsCode == "02");
        Assert.Empty(constructionManagement.WeeklyHours);
        Assert.Equal(["CA", "SO"], constructionManagement.Tasks.Select(t => t.WbsCode));

        ValidateAgainstSchema(input);
    }

    private static List<TimesheetRow> ParseFixture(string fileName)
    {
        using var reader = new StreamReader(Path.Combine(FixturesDir, fileName));
        return [.. TimesheetAggregator.ParseCsv(reader)];
    }

    private static void ValidateAgainstSchema(StaffingPlanInput input)
    {
        var validator = new SchemaValidator();
        var element = JsonSerializer.SerializeToElement(input, JsonOptions.Default);
        validator.ValidateBlobInput(element); // throws on failure
    }
}

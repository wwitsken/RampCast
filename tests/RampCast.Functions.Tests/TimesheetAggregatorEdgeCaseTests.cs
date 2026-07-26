using RampCast.Functions.Models;
using RampCast.Functions.Services;
using Xunit;

namespace RampCast.Functions.Tests;

/// <summary>
/// Precise edge cases for TimesheetAggregator, built from hand-constructed
/// TimesheetRow objects rather than CSV text so each scenario controls exactly
/// the dates/roles/hierarchy it needs.
/// </summary>
public class TimesheetAggregatorEdgeCaseTests
{
    private static TimesheetRow Row(
        string wbs1, string wbs1Name, DateOnly day, decimal hours,
        string wbs2 = "", string wbs2Name = "", string wbs3 = "", string wbs3Name = "",
        string role = "Project Architect", string comment = "")
        => new()
        {
            Wbs1 = wbs1,
            Wbs1Name = wbs1Name,
            Wbs2 = wbs2,
            Wbs2Name = wbs2Name,
            Wbs3 = wbs3,
            Wbs3Name = wbs3Name,
            Role = role,
            Day = day,
            Hours = hours,
            Comment = comment,
        };

    [Fact]
    public void OneProject_SplitAcrossTwoRowSets_MergesIntoOneProjectOnEarlierAnchor()
    {
        // Simulates the same project's rows arriving from two different uploaded
        // files: an earlier batch (the one that sets week 0) and a later one.
        var earlierFile = new[]
        {
            Row("R1", "Project One", new DateOnly(2024, 1, 15), 8, wbs2: "01", wbs2Name: "Phase One"),
        };
        var laterFile = new[]
        {
            Row("R1", "Project One", new DateOnly(2024, 1, 29), 8, wbs2: "01", wbs2Name: "Phase One"),
        };

        var combined = TimesheetAggregator.Aggregate([.. earlierFile, .. laterFile]);
        var project = Assert.Single(combined.Projects);

        // Week 0 anchors to the earlier file's Monday (2024-01-15); the later
        // row (2024-01-29, two ISO weeks after) must land at weekIndex 2, not
        // weekIndex 0 as it would if each file were anchored independently.
        var phase = Assert.Single(project.Phases);
        var weeks = phase.WeeklyHours.Select(w => w.WeekIndex).OrderBy(w => w).ToList();
        Assert.Equal([0, 2], weeks);
    }

    [Fact]
    public void ConflictingWbs1Name_ForSameWbs1_Throws()
    {
        var rows = new[]
        {
            Row("R1", "Project One", new DateOnly(2024, 1, 15), 8),
            Row("R1", "Project One (Renamed)", new DateOnly(2024, 1, 16), 8),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => TimesheetAggregator.Aggregate(rows));
        Assert.Contains("Project One", ex.Message);
        Assert.Contains("Project One (Renamed)", ex.Message);
    }

    [Fact]
    public void IsoWeekBoundary_SundayAndFollowingMonday_LandInDifferentWeeks()
    {
        // 2024-12-29 is a Sunday (end of ISO week 52); 2024-12-30 is the Monday
        // starting ISO week 1 of 2025 — a real year-boundary case the arithmetic
        // MondayOf() must get right without the ISOWeek year round-trip.
        var rows = new[]
        {
            Row("R1", "Project One", new DateOnly(2024, 12, 23), 8), // Monday: week 0 anchor
            Row("R1", "Project One", new DateOnly(2024, 12, 29), 8), // Sunday: still week 0
            Row("R1", "Project One", new DateOnly(2024, 12, 30), 8), // Monday: week 1
        };

        var input = TimesheetAggregator.Aggregate(rows);
        var project = Assert.Single(input.Projects);
        var weeks = project.WeeklyHours.Select(w => w.WeekIndex).OrderBy(w => w).ToList();

        Assert.Equal([0, 1], weeks);
    }

    [Fact]
    public void IsoWeekBoundary_TwoDaysInSameMondaySundaySpan_ShareOneWeek()
    {
        var rows = new[]
        {
            Row("R1", "Project One", new DateOnly(2024, 1, 15), 4), // Monday
            Row("R1", "Project One", new DateOnly(2024, 1, 21), 4), // following Sunday, same ISO week
        };

        var input = TimesheetAggregator.Aggregate(rows);
        var project = Assert.Single(input.Projects);
        var entry = Assert.Single(project.WeeklyHours);

        Assert.Equal(0, entry.WeekIndex);
        Assert.Equal(8, entry.Hours);
    }

    [Fact]
    public void DormantWeeks_AreCountedInDurationButNotMaterializedAsEntries()
    {
        // Hours only in week 0 and week 9 — durationWeeks must count the eight
        // dormant weeks between them, and weeklyHours stays sparse (2 entries,
        // not 10) rather than zero-filling the gap.
        var rows = new[]
        {
            Row("R1", "Project One", new DateOnly(2024, 1, 15), 8), // week 0
            Row("R1", "Project One", new DateOnly(2024, 3, 18), 8), // week 9 (9*7 days later)
        };

        var input = TimesheetAggregator.Aggregate(rows);
        var project = Assert.Single(input.Projects);

        Assert.Equal(10, project.DurationWeeks);
        Assert.Equal(9, project.LastChargedWeek);
        Assert.Equal(0, project.FirstChargedWeek);
        Assert.Equal(2, project.WeeklyHours.Count);
    }

    [Fact]
    public void UnphasedRows_CoexistWithPhases_BothPopulated()
    {
        // The Sunridge-shaped case: pre-award pursuit hours (blank wbs2) logged
        // before a phase breakdown exists, alongside a later populated phase.
        var rows = new[]
        {
            Row("R1", "Project One", new DateOnly(2024, 1, 8), 4, comment: "Pursuit debrief"),
            Row("R1", "Project One", new DateOnly(2024, 1, 22), 8, wbs2: "01", wbs2Name: "Schematic Design"),
        };

        var input = TimesheetAggregator.Aggregate(rows);
        var project = Assert.Single(input.Projects);

        Assert.NotEmpty(project.WeeklyHours);
        Assert.NotEmpty(project.Phases);
    }

    [Fact]
    public void PhaseWithNoTasks_IsPhaseLeaf_WeeklyHoursPopulatedTasksEmpty()
    {
        var rows = new[]
        {
            Row("R1", "Project One", new DateOnly(2024, 1, 15), 8, wbs2: "01", wbs2Name: "Phase One"),
        };

        var project = Assert.Single(TimesheetAggregator.Aggregate(rows).Projects);
        var phase = Assert.Single(project.Phases);

        Assert.NotEmpty(phase.WeeklyHours);
        Assert.Empty(phase.Tasks);
    }

    [Fact]
    public void PhaseWithTasks_IsTaskLeaf_PhaseWeeklyHoursEmpty()
    {
        var rows = new[]
        {
            Row("R1", "Project One", new DateOnly(2024, 1, 15), 8,
                wbs2: "01", wbs2Name: "Phase One", wbs3: "T1", wbs3Name: "Task One"),
        };

        var project = Assert.Single(TimesheetAggregator.Aggregate(rows).Projects);
        var phase = Assert.Single(project.Phases);

        Assert.Empty(phase.WeeklyHours);
        Assert.Single(phase.Tasks);
    }

    [Fact]
    public void MultipleProjects_OneCsvWorthOfRows_GroupIntoSeparateProjectsOrderedByWbsCode()
    {
        var rows = new[]
        {
            Row("R2", "Project Two", new DateOnly(2024, 2, 1), 8),
            Row("R1", "Project One", new DateOnly(2024, 1, 15), 8),
        };

        var input = TimesheetAggregator.Aggregate(rows);

        Assert.Equal(["R1", "R2"], input.Projects.Select(p => p.WbsCode));
        // Each project's own week 0 is independent of the other's.
        Assert.All(input.Projects, p => Assert.Equal(0, p.FirstChargedWeek));
    }
}

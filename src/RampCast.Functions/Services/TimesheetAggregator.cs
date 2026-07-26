using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using RampCast.Functions.Models;

namespace RampCast.Functions.Services;

/// <summary>
/// Parses timesheet CSV exports and aggregates them into the
/// blob-input-schema.json shape (project → phases → tasks → weeklyHours),
/// following docs/csv-input-schema.md. Stateless — no DI registration needed;
/// call the static members directly.
/// </summary>
public static class TimesheetAggregator
{
    /// <summary>Parse one CSV export into rows. Throws (via CsvHelper header
    /// validation) if any expected column, including wbsN_name, is missing.</summary>
    public static IReadOnlyList<TimesheetRow> ParseCsv(TextReader reader)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            // Empty cells for optional wbs2/wbs3 values are legal — don't treat
            // them as missing fields. Missing *columns* are still caught by the
            // default header validation.
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
        };
        using var csv = new CsvReader(reader, config);
        return [.. csv.GetRecords<TimesheetRow>()];
    }

    /// <summary>
    /// Aggregate parsed rows into the LLM input shape: a comparison set of
    /// projects, each on its own relative week axis. Rows for a given wbs1 may
    /// be spread across several uploaded files — that's the point, since a batch
    /// is meant to hold a whole set of comparable past projects the user picked.
    /// </summary>
    public static StaffingPlanInput Aggregate(IReadOnlyList<TimesheetRow> rows)
    {
        if (rows.Count == 0)
            throw new InvalidOperationException("No timesheet rows to aggregate.");

        foreach (var row in rows)
            ValidateRow(row);

        var projects = rows
            .GroupBy(r => r.Wbs1, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => BuildProject(g.Key, [.. g]))
            .ToList();

        return new StaffingPlanInput(projects);
    }

    private static ProjectNode BuildProject(string wbs1, List<TimesheetRow> rows)
    {
        // A batch merging rows from multiple files creates a new failure mode a
        // single-file batch never had: two unrelated projects sharing a
        // mistyped wbs1 would silently merge into one comparable. Catch it here
        // rather than let it corrupt the aggregation.
        var names = rows.Select(r => r.Wbs1Name).Distinct(StringComparer.Ordinal).ToList();
        if (names.Count > 1)
            throw new InvalidOperationException(
                $"Rows for project '{wbs1}' disagree on wbs1_name ({string.Join(" / ", names)}); " +
                "one project code must name one project across every uploaded file.");

        // Chronological order underpins comment ordering and the week-0 anchor.
        var ordered = rows.OrderBy(r => r.Day).ToList();

        // Week 0 is anchored project-wide — across every row for this project,
        // regardless of phase/task or which file it came from — so phases and
        // tasks stay comparable on one axis, mirroring the single project-wide
        // week axis output-plan-schema.json already uses for rampPattern.
        var weekZero = MondayOf(ordered[0].Day);
        int WeekIndex(DateOnly day) => (MondayOf(day).DayNumber - weekZero.DayNumber) / 7;

        var projectLevelRows = ordered.Where(r => Blank(r.Wbs2) && Blank(r.Wbs3)).ToList();

        var phases = new List<PhaseNode>();
        foreach (var phaseGroup in ordered
                     .Where(r => !Blank(r.Wbs2))
                     .GroupBy(r => r.Wbs2, StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var phaseRows = phaseGroup.ToList();
            var phaseName = phaseRows[0].Wbs2Name;
            var phaseLevelRows = phaseRows.Where(r => Blank(r.Wbs3)).ToList();

            var tasks = new List<TaskNode>();
            foreach (var taskGroup in phaseRows
                         .Where(r => !Blank(r.Wbs3))
                         .GroupBy(r => r.Wbs3, StringComparer.Ordinal)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var taskRows = taskGroup.ToList();
                tasks.Add(new TaskNode(
                    taskGroup.Key,
                    taskRows[0].Wbs3Name,
                    FirstWeek(taskRows, WeekIndex),
                    LastWeek(taskRows, WeekIndex),
                    BuildWeekly(taskRows, WeekIndex)));
            }

            phases.Add(new PhaseNode(
                phaseGroup.Key,
                phaseName,
                FirstWeek(phaseRows, WeekIndex),
                LastWeek(phaseRows, WeekIndex),
                // Phase weeklyHours are populated only when the phase has no task
                // breakdown; otherwise the leaf hours live on the tasks.
                BuildWeekly(phaseLevelRows, WeekIndex),
                tasks));
        }

        var lastChargedWeek = LastWeek(ordered, WeekIndex);

        return new ProjectNode(
            wbs1,
            ordered[0].Wbs1Name,
            weekZero.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            lastChargedWeek + 1, // durationWeeks: inclusive of week 0, dormant weeks still counted
            0, // firstChargedWeek is 0 by construction — week 0 is this project's own anchor
            lastChargedWeek,
            // Unphased hours (blank wbs2/wbs3 — e.g. pre-award pursuit work) can
            // legitimately coexist with a populated phases array, unlike the
            // phase/task leaf rule below: once a phase has tasks, ALL of its
            // hours live under them, but "no phase assigned yet" and "phases
            // exist" aren't mutually exclusive at the project level.
            BuildWeekly(projectLevelRows, WeekIndex),
            phases);
    }

    private static void ValidateRow(TimesheetRow row)
    {
        if (Blank(row.Wbs1))
            throw new InvalidOperationException("Timesheet row is missing a wbs1 project code.");
        if (Blank(row.Wbs1Name))
            throw new InvalidOperationException(
                $"Timesheet row for project '{row.Wbs1}' is missing wbs1_name; will not fall back to the WBS code as the name.");
        if (Blank(row.Role))
            throw new InvalidOperationException($"Timesheet row for '{row.Wbs1}' is missing a role.");

        // wbs3 without wbs2 is an invalid WBS hierarchy.
        if (Blank(row.Wbs2) && !Blank(row.Wbs3))
            throw new InvalidOperationException(
                $"Timesheet row has wbs3 '{row.Wbs3}' but no wbs2; invalid WBS hierarchy.");

        // Names must be present at each populated level — never derive from the code.
        if (!Blank(row.Wbs2) && Blank(row.Wbs2Name))
            throw new InvalidOperationException(
                $"Timesheet row for phase '{row.Wbs2}' is missing wbs2_name; will not fall back to the WBS code as the name.");
        if (!Blank(row.Wbs3) && Blank(row.Wbs3Name))
            throw new InvalidOperationException(
                $"Timesheet row for task '{row.Wbs3}' is missing wbs3_name; will not fall back to the WBS code as the name.");
    }

    private static IReadOnlyList<WeeklyHourEntry> BuildWeekly(
        IReadOnlyList<TimesheetRow> rows, Func<DateOnly, int> weekIndex)
        => [.. rows
            .GroupBy(r => (Week: weekIndex(r.Day), r.Role))
            // Role-major so each role's ramp reads as one contiguous run of
            // weeks in the serialized JSON — the shape the LLM is asked to read.
            .OrderBy(g => g.Key.Role, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Week)
            .Select(g => new WeeklyHourEntry(
                g.Key.Week,
                g.Key.Role,
                g.Sum(r => r.Hours),
                [.. g.OrderBy(r => r.Day)
                    .Select(r => r.Comment)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c.Trim())]))];

    /// <summary>Monday of the ISO week containing <paramref name="day"/>.</summary>
    private static DateOnly MondayOf(DateOnly day)
        => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private static int FirstWeek(IReadOnlyList<TimesheetRow> rows, Func<DateOnly, int> weekIndex)
        => weekIndex(rows.Min(r => r.Day));

    private static int LastWeek(IReadOnlyList<TimesheetRow> rows, Func<DateOnly, int> weekIndex)
        => weekIndex(rows.Max(r => r.Day));

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}

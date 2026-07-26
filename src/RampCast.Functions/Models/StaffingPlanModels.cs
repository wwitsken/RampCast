namespace RampCast.Functions.Models;

// --- Aggregated timesheet input (Schemas/blob-input-schema.json) ---
//
// Record property order determines JSON key order (System.Text.Json emits in
// declaration order), which is deliberately kept aligned with the schema's
// property order purely for LLM legibility — not a correctness requirement,
// but don't "clean up" the ordering without checking the schema first.

public sealed record StaffingPlanInput(
    IReadOnlyList<ProjectNode> Projects);

public sealed record ProjectNode(
    string WbsCode,
    string Name,
    string WeekZeroStart,
    int DurationWeeks,
    int FirstChargedWeek,
    int LastChargedWeek,
    IReadOnlyList<WeeklyHourEntry> WeeklyHours,
    IReadOnlyList<PhaseNode> Phases);

public sealed record PhaseNode(
    string WbsCode,
    string Name,
    int FirstChargedWeek,
    int LastChargedWeek,
    IReadOnlyList<WeeklyHourEntry> WeeklyHours,
    IReadOnlyList<TaskNode> Tasks);

public sealed record TaskNode(
    string WbsCode,
    string Name,
    int FirstChargedWeek,
    int LastChargedWeek,
    IReadOnlyList<WeeklyHourEntry> WeeklyHours);

public sealed record WeeklyHourEntry(
    int WeekIndex,
    string Role,
    decimal Hours,
    IReadOnlyList<string> Comments);

// The generated staffing plan (Schemas/output-plan-schema.json) lives in
// RampCast.DocGen.Models — see src/RampCast.DocGen/Models/StaffingPlan.cs.
// It's rendered by the shared doc-generator module, so it belongs there
// rather than here; this project references DocGen, not the reverse.

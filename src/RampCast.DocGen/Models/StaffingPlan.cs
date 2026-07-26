namespace RampCast.DocGen.Models;

// The generated staffing plan (Schemas/output-plan-schema.json in
// RampCast.Functions). This is the deserialization target for the Anthropic
// tool_use response in StaffingPlanGenerator, and the input to
// ExcelDocumentGenerator.

public sealed record StaffingPlan(
    string PlanSummary,
    int TotalDurationWeeks,
    IReadOnlyList<PlanPhase> Phases);

public sealed record PlanPhase(
    string WbsCode,
    string Name,
    IReadOnlyList<PlanRole> Roles,
    IReadOnlyList<PlanTask> Tasks);

public sealed record PlanTask(
    string WbsCode,
    string Name,
    IReadOnlyList<PlanRole> Roles);

public sealed record PlanRole(
    string RoleName,
    IReadOnlyList<decimal> RampPattern,
    decimal TotalHours,
    string Rationale);

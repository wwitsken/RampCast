namespace RampCast.Functions.Models;

/// <summary>
/// Queue message envelope for the batch-analysis queue. AnalyzeBatch serializes
/// this to JSON and base64-encodes it (Storage Queue messages must be
/// base64-encoded by default); GenerateStaffingPlan decodes it back.
/// </summary>
public sealed record BatchAnalysisMessage(string BatchId, string? Guidance);

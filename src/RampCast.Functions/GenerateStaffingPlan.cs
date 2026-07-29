using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using RampCast.DocGen;
using RampCast.Functions.Models;
using RampCast.Functions.Services;

namespace RampCast.Functions;

/// <summary>
/// Queue-triggered worker: for a batchId on the batch-analysis queue, download
/// every upload blob, aggregate the CSVs into the blob-input shape, call the LLM
/// to produce a staffing plan, and persist status + result.
/// </summary>
public class GenerateStaffingPlan(
    ILogger<GenerateStaffingPlan> logger,
    BlobServiceClient blobServiceClient,
    SchemaValidator schemaValidator,
    StaffingPlanGenerator planGenerator,
    PlanDocumentStore planStore,
    BatchStatusStore statusStore)
{
    private readonly ILogger<GenerateStaffingPlan> _logger = logger;
    private readonly BlobServiceClient _blobServiceClient = blobServiceClient;
    private readonly SchemaValidator _schemaValidator = schemaValidator;
    private readonly StaffingPlanGenerator _planGenerator = planGenerator;
    private readonly PlanDocumentStore _planStore = planStore;
    private readonly BatchStatusStore _statusStore = statusStore;

    [Function(nameof(GenerateStaffingPlan))]
    public async Task Run(
        // Deliberately a distinct connection name from the app's own "Storage"
        // options section (used by AddAzureClients in Program.cs) rather than
        // reusing "Storage" for this too: reusing it — a flat connection-string
        // value coexisting with Storage__blobServiceUri/etc. under the same
        // name — silently broke the trigger in production (listener registered,
        // nothing ever dispatched, no error anywhere). This exact shape
        // (identity-only, its own name) is the one confirmed working.
        [QueueTrigger("batch-analysis", Connection = "AzureQueueStorage")] QueueMessage message,
        CancellationToken cancellationToken)
    {
        // The queue trigger auto-decodes the base64 AnalyzeBatch applied, so
        // MessageText is the envelope JSON (or, for messages enqueued before the
        // envelope existed, a bare batchId).
        var (batchId, guidance) = ParseMessage(message.MessageText);
        _logger.LogInformation("Generating staffing plan for batch {BatchId}.", batchId);

        try
        {
            await _statusStore.WriteAsync(batchId, "processing", result: null, cancellationToken);

            var rows = await ReadTimesheetRowsAsync(batchId, cancellationToken);
            if (rows.Count == 0)
                throw new InvalidOperationException($"No upload blobs found for batch {batchId}.");

            var input = TimesheetAggregator.Aggregate(rows);

            // Validate the aggregated shape against blob-input-schema.json before
            // spending an LLM call on it.
            var inputElement = JsonSerializer.SerializeToElement(input, JsonOptions.Default);
            _schemaValidator.ValidateBlobInput(inputElement);

            _logger.LogInformation(
                "Batch {BatchId} aggregated into {ProjectCount} comparable project(s): {Codes}.",
                batchId, input.Projects.Count, string.Join(", ", input.Projects.Select(p => p.WbsCode)));

            var plan = await _planGenerator.GenerateAsync(input, guidance, cancellationToken);

            // Render the plan to .xlsx and upload it *before* marking complete, so a
            // "complete" status always has a downloadable document behind it.
            var workbook = ExcelDocumentGenerator.Generate(plan);
            await _planStore.WriteAsync(batchId, workbook, cancellationToken);

            await _statusStore.WriteAsync(batchId, "complete", plan, cancellationToken);
            _logger.LogInformation("Completed staffing plan for batch {BatchId}.", batchId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate staffing plan for batch {BatchId}.", batchId);

            // Best-effort failed status; then rethrow so Storage Queue retry/poison
            // (batch-analysis-poison, default 5 dequeues) handles it. Don't swallow.
            try
            {
                await _statusStore.WriteAsync(batchId, "failed", result: null, cancellationToken);
            }
            catch (Exception statusEx)
            {
                _logger.LogError(statusEx, "Failed to write failed status for batch {BatchId}.", batchId);
            }

            throw;
        }
    }

    /// <summary>
    /// AnalyzeBatch base64-encodes a JSON envelope carrying the batchId and
    /// optional guidance; the queue trigger auto-decodes the base64, so
    /// MessageText is the envelope JSON. Messages enqueued before the envelope
    /// existed are a bare batchId — still accepted so in-flight work drains
    /// cleanly. A batchId is a GUID, so a leading '{' unambiguously distinguishes
    /// the two.
    /// </summary>
    internal static (string BatchId, string? Guidance) ParseMessage(string messageText)
    {
        var text = messageText.Trim();

        if (text.StartsWith('{'))
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<BatchAnalysisMessage>(text, JsonOptions.Default);
                if (!string.IsNullOrWhiteSpace(envelope?.BatchId))
                    return (envelope!.BatchId, envelope.Guidance);
            }
            catch (JsonException)
            {
                // Fall through to the legacy interpretation.
            }
        }

        return (text, null);
    }

    private async Task<IReadOnlyList<TimesheetRow>> ReadTimesheetRowsAsync(string batchId, CancellationToken ct)
    {
        var container = _blobServiceClient.GetBlobContainerClient("uploads");
        var rows = new List<TimesheetRow>();

        await foreach (var blobItem in container.GetBlobsAsync(
                           BlobTraits.None, BlobStates.None, prefix: $"{batchId}/", cancellationToken: ct))
        {
            var blobClient = container.GetBlobClient(blobItem.Name);
            var download = await blobClient.DownloadContentAsync(ct);
            using var reader = new StreamReader(download.Value.Content.ToStream());
            rows.AddRange(TimesheetAggregator.ParseCsv(reader));
        }

        return rows;
    }
}

import type { UseQueryResult } from "@tanstack/react-query";
import type { BatchStatusResponse } from "../lib/api";
import { DownloadButton } from "./DownloadButton";

interface StatusPanelProps {
  batchId: string;
  query: UseQueryResult<BatchStatusResponse, Error>;
}

/**
 * Renders four distinct states so a status-query network failure, a
 * `failed` processing result, and success never collapse into one message.
 */
export function StatusPanel({ batchId, query }: StatusPanelProps) {
  if (query.isError) {
    return (
      <div className="rounded-lg border border-amber-300 bg-amber-50 p-4">
        <p className="text-sm font-semibold text-amber-800">Couldn't reach the server to check status.</p>
        <p className="mt-1 text-xs text-amber-700">{query.error.message}</p>
        <button
          type="button"
          onClick={() => query.refetch()}
          className="mt-2 rounded-md border border-amber-400 px-3 py-1 text-xs font-medium text-amber-800 hover:bg-amber-100"
        >
          Retry
        </button>
      </div>
    );
  }

  const status = query.data?.status;

  if (status === "complete") {
    return (
      <div className="rounded-lg border border-green-300 bg-green-50 p-4">
        <p className="mb-3 text-sm font-semibold text-green-800">Staffing plan is ready.</p>
        <DownloadButton batchId={batchId} serverUrl={query.data?.downloadUrl ?? null} />
      </div>
    );
  }

  if (status === "failed") {
    return (
      <div className="rounded-lg border border-red-300 bg-red-50 p-4">
        <p className="text-sm font-semibold text-red-800">Plan generation failed.</p>
        <p className="mt-1 text-xs text-red-700">
          Something went wrong while generating the staffing plan. Check the Functions host logs for the batch, then
          start over with a new batch.
        </p>
      </div>
    );
  }

  // queued | processing | not-yet-fetched
  return (
    <div className="flex items-center gap-3 rounded-lg border border-blue-300 bg-blue-50 p-4">
      <span className="h-3 w-3 shrink-0 animate-pulse rounded-full bg-blue-500" />
      <p className="text-sm font-medium text-blue-800">
        {status === "processing" ? "Generating your staffing plan…" : "Queued for analysis…"}
      </p>
    </div>
  );
}

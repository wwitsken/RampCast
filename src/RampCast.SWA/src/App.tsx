import { useCallback, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { analyzeBatch, getStatus, type AnalyzeResponse, type BatchStatusResponse } from "./lib/api";
import type { FileUploadStatus, TrackedFile } from "./types";
import { useAccessToken } from "./context/AccessTokenContext";
import { UploadZone } from "./components/UploadZone";
import { GuidanceInput } from "./components/GuidanceInput";
import { FileRow } from "./components/FileRow";
import { StatusPanel } from "./components/StatusPanel";
import { TokenGate } from "./components/TokenGate";
import { UsageCounter } from "./components/UsageCounter";

function App() {
  const { isReady, isValidating, uploadsRemaining, analysesRemaining, refreshUsage } = useAccessToken();

  const [batchId, setBatchId] = useState<string | null>(null);
  const [files, setFiles] = useState<TrackedFile[]>([]);
  const [statuses, setStatuses] = useState<Record<string, FileUploadStatus>>({});
  const [guidance, setGuidance] = useState("");

  const analyzeMutation = useMutation<AnalyzeResponse, Error>({
    mutationFn: () => analyzeBatch(batchId!, guidance),
    onSuccess: refreshUsage,
  });

  const statusQuery = useQuery<BatchStatusResponse, Error>({
    queryKey: ["status", batchId],
    queryFn: () => getStatus(batchId!),
    enabled: !!batchId && analyzeMutation.isSuccess,
    refetchInterval: (data) => (data?.status === "complete" || data?.status === "failed" ? false : 2000),
  });

  function handleFilesAdded(added: File[]) {
    setBatchId((current) => current ?? crypto.randomUUID());
    setFiles((current) => [...current, ...added.map((file) => ({ id: crypto.randomUUID(), file }))]);
  }

  const handleStatusChange = useCallback(
    (id: string, status: FileUploadStatus) => {
      setStatuses((current) => ({ ...current, [id]: status }));
      if (status === "uploaded") refreshUsage();
    },
    [refreshUsage],
  );

  function handleStartOver() {
    setBatchId(null);
    setFiles([]);
    setStatuses({});
    setGuidance("");
    analyzeMutation.reset();
  }

  const allUploaded = files.length > 0 && files.every((f) => statuses[f.id] === "uploaded");

  return (
    <div className="mx-auto min-h-screen max-w-3xl px-4 py-10">
      <header className="mb-8 flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">RampCast</h1>
          <p className="mt-1 text-sm text-gray-500">
            Upload historical timesheet exports to draft a data-backed staffing plan.
          </p>
        </div>
        {isReady && <UsageCounter />}
      </header>

      {!isReady ? (
        isValidating ? (
          <p className="text-sm text-gray-500">Checking access token…</p>
        ) : (
          <TokenGate />
        )
      ) : (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <UploadZone onFilesAdded={handleFilesAdded} disabled={uploadsRemaining <= 0} />
            <GuidanceInput value={guidance} onChange={setGuidance} />
          </div>

          {files.length > 0 && batchId && (
            <ul className="mt-6 flex flex-col gap-2">
              {files.map((tracked) => (
                <FileRow key={tracked.id} batchId={batchId} tracked={tracked} onStatusChange={handleStatusChange} />
              ))}
            </ul>
          )}

          {files.length > 0 && (
            <div className="mt-6">
              {!analyzeMutation.isSuccess ? (
                <div className="flex flex-col gap-2">
                  <div className="flex items-center gap-3">
                    <button
                      type="button"
                      disabled={!allUploaded || analyzeMutation.isLoading || analysesRemaining <= 0}
                      onClick={() => analyzeMutation.mutate()}
                      className="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-gray-300"
                    >
                      {analyzeMutation.isLoading ? "Starting…" : "Generate Plan"}
                    </button>
                    {analyzeMutation.isError && (
                      <p className="text-sm text-red-700">Couldn't start analysis: {analyzeMutation.error.message}</p>
                    )}
                  </div>
                  {analysesRemaining <= 0 && (
                    <p className="text-sm text-amber-700">This token has no analyses remaining.</p>
                  )}
                </div>
              ) : (
                batchId && (
                  <div className="flex flex-col gap-3">
                    <StatusPanel batchId={batchId} query={statusQuery} />
                    <button
                      type="button"
                      onClick={handleStartOver}
                      className="self-start text-sm font-medium text-gray-500 hover:text-gray-700"
                    >
                      Start over with a new batch
                    </button>
                  </div>
                )
              )}
            </div>
          )}
        </>
      )}
    </div>
  );
}

export default App;

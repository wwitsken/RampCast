import { useEffect, useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { uploadFile, type UploadResponse } from "../lib/api";
import type { FileUploadStatus, TrackedFile } from "../types";
import { StatusBadge } from "./StatusBadge";

interface FileRowProps {
  batchId: string;
  tracked: TrackedFile;
  onStatusChange: (id: string, status: FileUploadStatus) => void;
}

/**
 * Owns a single file's upload mutation so each file can retry independently
 * without restarting the whole batch. Auto-uploads once on mount; the
 * startedRef guard keeps React 18 StrictMode's dev-mode double-effect from
 * firing the upload twice. Status is set explicitly from the mutation's
 * onMutate/onSuccess/onError callbacks (fired exactly when the request is
 * sent/settles) rather than derived from isLoading/isSuccess/isError on
 * render, so the badge updates the moment the response comes back.
 */
export function FileRow({ batchId, tracked, onStatusChange }: FileRowProps) {
  const { id, file } = tracked;
  const startedRef = useRef(false);
  const [status, setStatus] = useState<FileUploadStatus>("pending");

  function updateStatus(next: FileUploadStatus) {
    setStatus(next);
    onStatusChange(id, next);
  }

  const mutation = useMutation<UploadResponse, Error>({
    mutationFn: () => uploadFile(batchId, file),
    onMutate: () => updateStatus("uploading"),
    onSuccess: () => updateStatus("uploaded"),
    onError: () => updateStatus("failed"),
  });

  // Surface the server's validation detail (e.g. "missing wbs1_name column")
  // rather than just a bare "failed" badge — upload-time validation rejects a
  // lot more than it used to, so a silent failure is a much worse experience now.
  const errorMessage = mutation.isError ? mutation.error.message : null;

  useEffect(() => {
    if (startedRef.current) return;
    startedRef.current = true;
    mutation.mutate();
    // Fire-once-on-mount by design; mutate is stable per useMutation instance.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <li className="flex flex-col gap-1 rounded-md border border-gray-200 px-3 py-2">
      <div className="flex items-center justify-between gap-3">
        <span className="min-w-0 truncate text-sm text-gray-800" title={file.name}>
          {file.name}
        </span>
        <div className="flex shrink-0 items-center gap-2">
          {status === "failed" && (
            <button
              type="button"
              onClick={() => mutation.mutate()}
              className="rounded-md border border-red-300 px-2 py-0.5 text-xs font-medium text-red-700 hover:bg-red-50"
            >
              Retry
            </button>
          )}
          <StatusBadge status={status} />
        </div>
      </div>
      {status === "failed" && errorMessage && (
        <p className="text-xs text-red-700">{errorMessage}</p>
      )}
    </li>
  );
}

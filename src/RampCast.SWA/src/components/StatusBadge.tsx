import type { FileUploadStatus } from "../types";

const STYLES: Record<FileUploadStatus, string> = {
  pending: "bg-gray-100 text-gray-600",
  uploading: "bg-blue-100 text-blue-700",
  uploaded: "bg-green-100 text-green-700",
  failed: "bg-red-100 text-red-700",
};

const LABELS: Record<FileUploadStatus, string> = {
  pending: "Pending",
  uploading: "Uploading…",
  uploaded: "Uploaded",
  failed: "Failed",
};

export function StatusBadge({ status }: { status: FileUploadStatus }) {
  return (
    <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium whitespace-nowrap ${STYLES[status]}`}>
      {LABELS[status]}
    </span>
  );
}

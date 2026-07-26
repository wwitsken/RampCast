import { useMutation } from "@tanstack/react-query";
import { downloadPlan } from "../lib/api";

interface DownloadButtonProps {
  batchId: string;
  serverUrl: string | null;
}

// A plain <a href download> can't carry the access-token header, so the
// download is fetched with the header attached and saved via a temporary
// object URL instead of direct browser navigation.
export function DownloadButton({ batchId, serverUrl }: DownloadButtonProps) {
  const mutation = useMutation<void, Error>({
    mutationFn: async () => {
      const blob = await downloadPlan(batchId, serverUrl);
      const url = URL.createObjectURL(blob);
      try {
        const link = document.createElement("a");
        link.href = url;
        link.download = `${batchId}-staffing-plan.xlsx`;
        link.click();
      } finally {
        URL.revokeObjectURL(url);
      }
    },
  });

  return (
    <div className="flex flex-col items-start gap-1">
      <button
        type="button"
        onClick={() => mutation.mutate()}
        disabled={mutation.isLoading}
        className="inline-flex items-center gap-2 rounded-md bg-green-600 px-4 py-2 text-sm font-semibold text-white hover:bg-green-700 disabled:cursor-not-allowed disabled:bg-gray-300"
      >
        {mutation.isLoading ? "Preparing download…" : "Download staffing plan (.xlsx)"}
      </button>
      {mutation.isError && <p className="text-sm text-red-700">Couldn't download the plan: {mutation.error.message}</p>}
    </div>
  );
}

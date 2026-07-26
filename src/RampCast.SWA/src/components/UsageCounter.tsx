import { useAccessToken } from "../context/AccessTokenContext";

function pillStyle(remaining: number, total: number): string {
  if (remaining <= 0) return "bg-red-100 text-red-700";
  if (remaining <= total * 0.2) return "bg-amber-100 text-amber-700";
  return "bg-green-100 text-green-700";
}

export function UsageCounter() {
  const { usage, signOut } = useAccessToken();

  if (!usage) return null;

  return (
    <div className="flex flex-wrap items-center gap-2">
      <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium whitespace-nowrap ${pillStyle(usage.uploadsRemaining, usage.uploadGrants)}`}>
        Uploads {usage.uploadsRemaining} / {usage.uploadGrants}
      </span>
      <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium whitespace-nowrap ${pillStyle(usage.analysesRemaining, usage.analysisGrants)}`}>
        Analyses {usage.analysesRemaining} / {usage.analysisGrants}
      </span>
      <button
        type="button"
        onClick={signOut}
        className="text-sm font-medium text-gray-500 hover:text-gray-700"
      >
        Use a different token
      </button>
    </div>
  );
}

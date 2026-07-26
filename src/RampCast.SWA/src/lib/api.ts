// Thin typed wrapper around the RampCast Functions API. Every call throws a
// plain Error with the response body as its message on a non-2xx status, so
// callers (TanStack Query mutations/queries) get a real message to surface.

const API_BASE = import.meta.env.VITE_API_BASE ?? "/api";

const TOKEN_HEADER = "X-RampCast-Token";

let accessToken: string | null = null;

export function setAccessToken(token: string | null): void {
  accessToken = token;
}

function tokenHeaders(extra?: Record<string, string>): Record<string, string> {
  return accessToken ? { ...extra, [TOKEN_HEADER]: accessToken } : { ...extra };
}

export class ApiError extends Error {
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

async function handle<T>(res: Response): Promise<T> {
  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new ApiError(body || `Request failed with status ${res.status}`, res.status);
  }
  return (await res.json()) as T;
}

async function handleBlob(res: Response): Promise<Blob> {
  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new ApiError(body || `Request failed with status ${res.status}`, res.status);
  }
  return res.blob();
}

export interface UploadResponse {
  batchId: string;
  fileName: string;
}

export interface AnalyzeResponse {
  batchId: string;
  result: string;
  fileCount: number;
}

export type BatchStatusValue = "queued" | "processing" | "complete" | "failed";

export interface BatchStatusResponse {
  batchId: string;
  status: BatchStatusValue;
  result: unknown | null;
  downloadUrl: string | null;
}

export interface TokenUsage {
  uploadsRemaining: number;
  uploadGrants: number;
  analysesRemaining: number;
  analysisGrants: number;
  expiresAt: string | null;
}

export async function uploadFile(batchId: string, file: File): Promise<UploadResponse> {
  const res = await fetch(`${API_BASE}/upload/${batchId}?fileName=${encodeURIComponent(file.name)}`, {
    method: "POST",
    headers: tokenHeaders({ "Content-Type": file.type || "text/csv" }),
    body: file,
  });
  return handle<UploadResponse>(res);
}

export async function analyzeBatch(batchId: string, guidance: string): Promise<AnalyzeResponse> {
  const res = await fetch(`${API_BASE}/analyze/${batchId}`, {
    method: "POST",
    headers: tokenHeaders({ "Content-Type": "application/json" }),
    body: JSON.stringify({ guidance }),
  });
  return handle<AnalyzeResponse>(res);
}

export async function getStatus(batchId: string): Promise<BatchStatusResponse> {
  const res = await fetch(`${API_BASE}/status/${batchId}`, { headers: tokenHeaders() });
  return handle<BatchStatusResponse>(res);
}

export function downloadUrlFor(batchId: string, serverUrl: string | null): string {
  return serverUrl ?? `${API_BASE}/plans/${batchId}`;
}

export async function downloadPlan(batchId: string, serverUrl: string | null): Promise<Blob> {
  const res = await fetch(downloadUrlFor(batchId, serverUrl), { headers: tokenHeaders() });
  return handleBlob(res);
}

// override lets the token-entry gate validate a pasted token before it's
// committed to app state (setAccessToken hasn't been called yet at that point).
export async function getTokenUsage(override?: string): Promise<TokenUsage> {
  const res = await fetch(`${API_BASE}/tokens/usage`, {
    headers: override ? { [TOKEN_HEADER]: override } : tokenHeaders(),
  });
  return handle<TokenUsage>(res);
}

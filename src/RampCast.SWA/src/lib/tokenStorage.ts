// Persists the access token across reloads. Wrapped in try/catch because
// localStorage throws in some private-browsing modes (e.g. Safari) rather
// than silently no-oping — a thrown storage error shouldn't crash the app.

const STORAGE_KEY = "rampcast.access-token";

export function readStoredToken(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

export function writeStoredToken(token: string): void {
  try {
    localStorage.setItem(STORAGE_KEY, token);
  } catch {
    // Best-effort; the token still works for the current session via state.
  }
}

export function clearStoredToken(): void {
  try {
    localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Nothing to clean up if storage isn't available.
  }
}

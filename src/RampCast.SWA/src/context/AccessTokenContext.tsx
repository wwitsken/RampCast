import { createContext, useCallback, useContext, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ApiError, getTokenUsage, setAccessToken, type TokenUsage } from "../lib/api";
import { clearStoredToken, readStoredToken, writeStoredToken } from "../lib/tokenStorage";

interface AccessTokenContextValue {
  isReady: boolean;
  isValidating: boolean;
  usage: TokenUsage | undefined;
  uploadsRemaining: number;
  analysesRemaining: number;
  signIn: (rawToken: string) => Promise<void>;
  signOut: () => void;
  refreshUsage: () => void;
}

const AccessTokenContext = createContext<AccessTokenContextValue | null>(null);

export function AccessTokenProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();

  // Hydrate from localStorage on first render and arm the api.ts fetch
  // wrapper immediately, so no request escapes without the header once a
  // token was already persisted from a previous session.
  const [token, setToken] = useState<string | null>(() => {
    const stored = readStoredToken();
    setAccessToken(stored);
    return stored;
  });

  const signOut = useCallback(() => {
    clearStoredToken();
    setAccessToken(null);
    setToken(null);
    queryClient.removeQueries(["token-usage"]);
  }, [queryClient]);

  const usageQuery = useQuery<TokenUsage, Error>({
    queryKey: ["token-usage", token],
    queryFn: () => getTokenUsage(),
    enabled: !!token,
    retry: false,
    // A 401 means the stored token is no longer valid (deleted/expired) —
    // drop it and fall back to the gate. A 403 (out of quota) is left alone;
    // the token is still valid, just exhausted.
    onError: (error) => {
      if (error instanceof ApiError && error.status === 401) signOut();
    },
  });

  const signIn = useCallback(
    async (rawToken: string) => {
      const usage = await getTokenUsage(rawToken);
      writeStoredToken(rawToken);
      setAccessToken(rawToken);
      queryClient.setQueryData(["token-usage", rawToken], usage);
      setToken(rawToken);
    },
    [queryClient],
  );

  const refreshUsage = useCallback(() => {
    queryClient.invalidateQueries(["token-usage"]);
  }, [queryClient]);

  const value = useMemo<AccessTokenContextValue>(
    () => ({
      isReady: !!token && usageQuery.isSuccess,
      isValidating: !!token && usageQuery.isLoading,
      usage: usageQuery.data,
      uploadsRemaining: usageQuery.data?.uploadsRemaining ?? 0,
      analysesRemaining: usageQuery.data?.analysesRemaining ?? 0,
      signIn,
      signOut,
      refreshUsage,
    }),
    [token, usageQuery.isSuccess, usageQuery.isLoading, usageQuery.data, signIn, signOut, refreshUsage],
  );

  return <AccessTokenContext.Provider value={value}>{children}</AccessTokenContext.Provider>;
}

// eslint-disable-next-line react-refresh/only-export-components -- the hook is the intended public API for this context
export function useAccessToken(): AccessTokenContextValue {
  const context = useContext(AccessTokenContext);
  if (!context) throw new Error("useAccessToken must be used within an AccessTokenProvider");
  return context;
}

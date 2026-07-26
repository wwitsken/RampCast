/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Base URL the frontend calls for the Functions API. Defaults to "/api"
   *  (same-origin, via the SWA CLI's reverse proxy) when unset. */
  readonly VITE_API_BASE?: string;
}

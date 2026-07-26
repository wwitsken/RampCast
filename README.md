# RampCast

RampCast mines historical Vantagepoint timesheet exports to draft a data-backed
staffing plan for a new project — phases, typical tasks, hours by role, and how
staffing ramps over time — using an Azure Functions pipeline that aggregates the
raw timesheet data and hands it to Claude with a forced structured-output tool
call. See [docs/staffing-plan-generator-summary.md](docs/staffing-plan-generator-summary.md)
for the full background, and [docs/README.md](docs/README.md) for the schema and
sample docs index.

## Pipeline

| Stage | Function               | Trigger                           | What it does                                                                                                                                                                                                                                                              |
| ----- | ---------------------- | --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1     | `UploadFile`           | `POST /api/upload/{batchId?}`     | Validates a timesheet CSV (columns, WBS hierarchy, schema) and stores it to blob storage under `uploads/{batchId}/`.                                                                                                                                                     |
| 2     | `AnalyzeBatch`         | `POST /api/analyze/{batchId}`     | Enqueues `{batchId, guidance}` onto the `batch-analysis` storage queue.                                                                                                                                                                                                   |
| 3     | `GenerateStaffingPlan` | Queue trigger on `batch-analysis` | Downloads every upload for the batch, parses + aggregates the CSVs into a comparison set of projects, validates against `blob-input-schema.json`, calls the Anthropic API with a forced tool matching `output-plan-schema.json`, renders the plan to an `.xlsx`, and writes status. |
| 4     | `GetBatchStatus`       | `GET /api/status/{batchId}`       | Reads back `{ batchId, status, result, downloadUrl }`, where `status` is `queued` → `processing` → `complete` \| `failed`. `downloadUrl` is populated once complete.                                                                                                      |
| 5     | `DownloadPlan`         | `GET /api/plans/{batchId}`        | Streams the generated staffing-plan `.xlsx` for the batch.                                                                                                                                                                                                                |

Every stage above except `GenerateStaffingPlan` (which never sees an HTTP
request) requires the `X-RampCast-Token` header — see [Access tokens](#access-tokens).

## Access tokens

Uploads and analysis are gated behind a self-issued access token — a GUID
carrying a quota of uploads and analyses, stored in the `AccessTokens` Azure
Table (auto-created on first mint, alongside the `uploads`/`status`/`plans`
blob containers and the `batch-analysis` queue — no manual provisioning
needed). The frontend won't reveal the upload zone until a pasted token
validates, and shows a live "N remaining" counter for both quotas.

| Function          | Trigger                     | Auth level  | What it does                                                                                     |
| ------------------ | ---------------------------- | ----------- | -------------------------------------------------------------------------------------------------- |
| `MintAccessToken`   | `POST /api/tokens`           | `Admin`     | Mints a new token. Body is optional JSON `{ uploadGrants, analysisGrants, expiresInDays }`; defaults to 25 uploads / 5 analyses / no expiration. |
| `GetTokenUsage`     | `GET /api/tokens/usage`      | Anonymous   | Reads `X-RampCast-Token` and reports remaining/total uploads and analyses for that token.        |

`MintAccessToken` is `Admin`-level, so it's called with the Function App's
master key (`?code=...` in production, no code needed against the local host).

Mint one against the local dev host (`npm run dev`/`npm run dev:functions` serves it on
port `7102`) with the default 25 uploads / 5 analyses / no expiration:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:7102/api/tokens"
```

Pass a body to override the defaults:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:7102/api/tokens" `
  -ContentType "application/json" `
  -Body (@{ uploadGrants = 5; analysisGrants = 2; expiresInDays = 7 } | ConvertTo-Json)
```

Against a deployed Function App, the same call needs the master key:

```bash
curl -X POST "https://<app>.azurewebsites.net/api/tokens?code=<master-key>"
# {"token":"3f2a...","uploadGrants":25,"analysisGrants":5,"expiresAt":null}
```

Every other endpoint validates the token before doing any work; `UploadFile`
and `AnalyzeBatch` only decrement the token's quota after the request
succeeds, so a rejected upload (bad CSV) or a failed analyze never burns a
grant. An invalid/expired token gets a `401` (frontend drops it and returns to
the token-entry screen); a valid-but-exhausted token gets a `403` (frontend
keeps the token and shows an "out of quota" message).

## Project layout

```
package.json               Dev orchestration only (npm run dev) — see below
src/
  RampCast.Functions/       Azure Functions app (.NET 10, isolated worker)
    Models/                   DTOs for the blob-input / output-plan / CSV shapes
    Services/                 TimesheetAggregator, SchemaValidator, StaffingPlanGenerator, PlanDocumentStore, BatchStatusStore, AuthTokenStore, AccessTokenService
    Schemas/                  blob-input-schema.json, output-plan-schema.json (runtime dependency of the code)
  RampCast.DocGen/          Shared doc-generation module — ExcelDocumentGenerator + the StaffingPlan model
  RampCast.SWA/              Frontend (React + Vite), deployed as an Azure Static Web App
tests/
  RampCast.Functions.Tests/ xunit tests for the aggregation/validation pipeline
docs/                       Project background, CSV contract, and sample fixtures
```

This is a standalone Functions app, not SWA's built-in/managed Functions — it
needs queue triggers and .NET, which SWA's managed Functions don't support. See
[Deployment](#deployment) for what that means for hooking the two up.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) + npm (for the dev orchestration script and the SWA frontend)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) (local Azure Storage emulator) — `npm install -g azurite`
- An Anthropic API key

## Setup

1. Install root dev-orchestration dependencies:

   ```powershell
   npm install
   ```

2. Set `ANTHROPIC_API_KEY` in `src/RampCast.Functions/local.settings.json` (already git-ignored):

   ```json
   {
     "IsEncrypted": false,
     "Values": {
       "AzureWebJobsStorage": "UseDevelopmentStorage=true",
       "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
       "ANTHROPIC_API_KEY": "sk-ant-..."
     },
     "Host": {
       "CORS": "http://localhost:4280,http://localhost:5173",
       "CORSCredentials": false
     }
   }
   ```

   The `Host.CORS` entry lets the browser call this API locally — since it's a
   standalone Functions app rather than SWA's managed Functions, the frontend
   calls it directly (`src/RampCast.SWA/.env` sets `VITE_API_BASE` to
   `http://127.0.0.1:7102/api`), which is a cross-origin request the browser
   blocks without it. Both origins matter: `4280` is the SWA CLI emulator's
   port, `5173` is Vite's own dev server port if you ever hit it directly. This
   setting is local-only — see [Deployment](#deployment) for the deployed
   equivalent.

3. Verify everything builds:

   ```powershell
   dotnet build
   ```

## Running locally

```powershell
# Terminal A — storage emulator
npm run dev:azurite

# Terminal B — Functions API + SWA frontend together, from the repo root
npm run dev
```

`npm run dev` runs both dev servers concurrently in one terminal (colored,
prefixed output; Ctrl+C stops both). Open **http://localhost:4280** — that's
the SWA CLI's own dev server, serving the frontend and matching what the
deployed SWA will actually route.

Individual pieces, if you need to run just one: `npm run dev:functions`
(Functions only) and `npm run dev:swa` (frontend only).

## Tests

```powershell
dotnet test
```

Covers `TimesheetAggregator` against the real sample CSVs in
`docs/samples/timesheets/` (multi-project grouping, per-project relative week
anchoring, ISO week/year boundaries, dormant-week duration counting), schema
validation of the aggregated output, and the batch-analysis queue message
parsing. See `tests/RampCast.Functions.Tests/`.

`AccessTokenTests.cs` covers the token-gate's pure logic (GUID normalization,
header parsing, remaining-quota math, mint-request defaults/validation) with
no external dependency. `AuthTokenStoreAzuriteTests.cs` round-trips
`AuthTokenStore` against a real Table Storage endpoint and skips cleanly
(rather than failing) when Azurite isn't running — start it first
(`npm run dev:azurite` or `npm run dev`) to exercise those.

## Deployment

**Functions** deploys as its own standalone Azure Function App resource (via
the Azure Functions VS Code extension, `func azure functionapp publish`, or
CI) — it is *not* deployed as part of the SWA build.

**SWA** needs to know where that Function App lives, since there's no
managed-Functions integration to route `/api/*` automatically. Set
`VITE_API_BASE` in `src/RampCast.SWA/.env.production` to the deployed Function
App's URL before building:

```
VITE_API_BASE=https://<your-function-app-name>.azurewebsites.net/api
```

This is a Vite build-time env var — it gets baked into the static bundle, so it
must be set before `npm run build` runs, not configured after deploy. The file
is committed (it's a URL, not a secret) with a placeholder; replace the
placeholder once the Function App is deployed and its real hostname is known.

The Function App also needs the SWA's deployed origin allowed in its CORS
settings (Azure Portal → the Function App → CORS, or
`az functionapp cors add --allowed-origins https://<your-swa-name>.azurestaticapps.net`) —
same requirement as the local `Host.CORS` setting above, just configured on the
Azure resource instead of `local.settings.json`.

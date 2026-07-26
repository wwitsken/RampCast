# RampCast docs

- [staffing-plan-generator-summary.md](staffing-plan-generator-summary.md) — project background and pipeline overview.
- [csv-input-schema.md](csv-input-schema.md) — source timesheet CSV column definitions and the parsing/aggregation rules the pipeline follows.

## Schemas

JSON Schema (draft 2020-12) contracts for the pipeline's data shapes. These are a runtime dependency of the code, not just documentation, so they live in the project source tree rather than here — see [`src/RampCast.Functions/Schemas/`](../src/RampCast.Functions/Schemas/). `RampCast.Functions.csproj` copies both into the build output and reads them at runtime (`SchemaValidator`, `StaffingPlanGenerator`).

- [blob-input-schema.json](../src/RampCast.Functions/Schemas/blob-input-schema.json) — aggregated timesheet input for a set of comparable past projects (`projects[]` → phases → tasks → weeklyHours), each on its own relative week axis, sent to the LLM.
- [output-plan-schema.json](../src/RampCast.Functions/Schemas/output-plan-schema.json) — the generated staffing plan; also used to build the forced tool definition for the Anthropic call.

## samples/

Concrete examples of the schemas above, used as reference and as fixtures.

- [samples/sample-timesheet.csv](samples/sample-timesheet.csv) — a raw timesheet export covering the phase-leaf and task-leaf aggregation cases.
- [samples/blob-input-sample.json](samples/blob-input-sample.json) — aggregated input example (phase-leaf and task-leaf cases).
- [samples/blob-input-sample-project-only.json](samples/blob-input-sample-project-only.json) — aggregated input example for the project-level-only leaf case (`phases: []`).
- [samples/output-plan-sample.json](samples/output-plan-sample.json) — example generated staffing plan.

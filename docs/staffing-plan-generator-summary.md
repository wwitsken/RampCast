# Staffing plan generator — project summary

## The idea

AEC firms sit on years of unstructured data that nobody mines: the free-text comment field on every timesheet entry. Combined with hours, role, phase, and date, that data implicitly encodes *how projects actually get staffed* — which nobody has ever turned into a reusable template.

This project selects a handful of comparable past projects, mines their timesheet hours and comments, and generates a draft staffing plan — phases, typical tasks, typical hours by role, and how staffing ramps over time — that a PM can review and adapt for a new project of the same type.

## Why this is a strong project

**Genuinely useful.** Every PM starting a new project either builds a staffing plan from memory or copies an old one and hopes it still applies. This gives them a data-backed starting point instead of a guess.

**Good niche.** Nobody is mining this data today — not Deltek, not competing consultants. Structured CRM fields get reported on constantly; the comment field is ignored. That's real, unclaimed territory.

**Manageable scope.** The design deliberately avoids the traps that would blow this up:
- Vantagepoint-agnostic pipeline instead of a generic multi-system sync engine — no adapter framework to design and maintain
- User hand-picks the comparison set instead of building automatic project-similarity matching — skips the hardest, most fragile part entirely
- Manual CSV export to start instead of a live webhook — proves the concept before investing in integration plumbing
- One LLM call with a forced JSON schema, not a multi-stage pattern-detection pipeline

**Productizable.** Once proven, this is sellable to any AEC firm on any system — the pipeline only needs hours, role, project/phase, date, and comments, fields every timesheet system has. The VP-specific version (a custom hub UI) becomes a thin front door onto the same core engine, not a rebuild.

**Strong developer signal.** It combines T-SQL-caliber data aggregation, an LLM structured-extraction pattern, and document generation — the same architecture reused across your portfolio projects rather than three disconnected demos. It's also a genuinely novel pitch: "I mined an ignored data source to solve a real planning problem" reads very differently from another CRUD integration.

## Architecture

Six-stage pipeline, all Azure Functions, all reusable patterns across your other portfolio projects:

1. **Upload CSV/JSON** — timesheet export for the selected comparison projects
2. **Blob storage** — stores the raw file
3. **Azure Function — parse & aggregate** — groups rows into a comparison set of projects, then within each by phase + role + week (not just phase + role), preserving:
   - Each phase's first/last charged week, duration, sequence order, and overlaps with other phases — all relative to that project's own week 0, so projects of very different lengths and calendar eras are still directly comparable by ramp position
   - Weekly hour distribution per role (the ramp-up/peak/taper shape, not a flat total)
   - Comments kept chronological and attached to their week, not dumped in an unordered bag
4. **Claude API** — forced tool-use JSON schema, turns the aggregated data into a structured plan: phases, typical tasks per phase (synthesized from the comments), hours by role, typical timing
5. **Doc generator** — shared module (reused by the RFP compliance extractor) that renders the JSON into a Word or Excel document
6. **Staffing plan output** — a draft document a PM reviews and edits, not an autopilot deliverable

## Data strategy

Deltek's demo database gives realistic *structure* (project types, phases, roles, hour distributions) but its stock comments are too thin to be useful test data. Plan: pull the structural skeleton from the demo database, discard its comments entirely, and generate replacement comments with an LLM — conditioned on each real (project type, phase, role) combination, with deliberately varied writing styles per synthetic employee (terse, detailed, boilerplate-prone) so the dataset actually stress-tests the extraction logic instead of being uniformly clean.

## Build roadmap

1. Build the synthetic dataset (Deltek demo structure + LLM-generated comments)
2. Build stage 3 (parse & aggregate) against synthetic data; verify the grouped JSON output by inspection before touching the LLM
3. Build stage 4 (Claude API call + schema) and stage 5 (doc generator module) against that synthetic data
4. Run the standard Vantagepoint report manually for a few real comparable projects, export to CSV, feed through the finished pipeline
5. Evaluate whether the generated plans are actually useful — this is the real validation step
6. Only after that: consider webhook automation and/or a Vantagepoint custom hub front end, and have the data-governance conversation with FSP before pointing the pipeline at live client data

## Guardrails

- Real project timesheet data is client-sensitive — validate on synthetic data first, get FSP sign-off before using real data
- Resist expanding scope toward automatic project-similarity matching or a generic multi-system sync layer — both were deliberately cut to keep this shippable
- The doc generator module should be built standalone from the start, so it's reusable rather than tangled into this project's Function

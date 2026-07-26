# CSV input schema — timesheet export

Source file for the staffing plan pipeline: a flat, daily-grain timesheet
export. This is parsed and aggregated into the shape defined by
`docs/blob-input-schema.json` — it is not a 1:1 mapping, see Requirements
below.

## Columns

| Column      | Type                | Required | Description                                                                                         |
| ----------- | ------------------- | -------- | --------------------------------------------------------------------------------------------------- |
| `wbs1`      | string              | yes      | Project-level WBS code                                                                              |
| `wbs1_name` | string              | yes      | Project name                                                                                        |
| `wbs2`      | string              | no       | Phase-level WBS code — blank when hours are charged directly to the project with no phase breakdown |
| `wbs2_name` | string              | no       | Phase name — blank whenever `wbs2` is blank                                                         |
| `wbs3`      | string              | no       | Task-level WBS code — blank when hours are charged directly to the phase with no task breakdown     |
| `wbs3_name` | string              | no       | Task name — blank whenever `wbs3` is blank                                                          |
| `role`      | string              | yes      | Role/title of the person who charged the hours                                                      |
| `day`       | date (`YYYY-MM-DD`) | yes      | The specific day the hours were charged                                                             |
| `hours`     | number              | yes      | Hours charged, this row                                                                             |
| `comment`   | string              | no       | Free-text timesheet comment, one row = one comment                                                  |

## Requirements for the parsing/aggregation step

1. **Weekly bucketing, relative to each project.** `day` is daily-grain;
   `blob-input-schema.json` wants `weeklyHours` bucketed by `weekIndex` — a
   0-based week offset from that *project's own* week 0, not a calendar date.
   Group rows by ISO week (Monday start) as before, then re-express each
   bucket's week as an offset from the project's own first charged week (see
   point 1a). Don't pass daily rows through as if they were already weekly,
   and don't leave the bucket as an absolute calendar week.

   1a. **Multi-project grouping.** A batch is meant to hold a whole set of
       comparable past projects the user picked, not just one — group rows by
       `wbs1` before aggregating, and aggregate each group independently. One
       exported file may contain rows for several projects, and rows for one
       project may be spread across several files; both must work. All rows
       sharing a `wbs1` must agree on `wbs1_name` — a mismatch is a
       data-quality error (most likely a mistyped `wbs1`) and must fail
       loudly rather than silently pick one name, since it would otherwise
       merge two unrelated projects into one comparable.

   1b. **Per-project week-0 anchor.** Week 0 for a project is the Monday of
       the ISO week containing that project's earliest charged `day`,
       computed across *all* of that project's rows regardless of phase,
       task, or source file. Phases and tasks index onto their parent
       project's axis, not their own — this is what keeps them comparable to
       each other and to other projects' phases/tasks by ramp position
       instead of by calendar date. Report the project's `durationWeeks` as
       `lastChargedWeek + 1` (inclusive), and don't compress dormant weeks
       out of that count — a mid-project gap is real signal about the ramp
       shape, not noise to remove.

2. **Comment aggregation.** Multiple rows can share the same
   `wbs1/wbs2/wbs3/role/week`, each with its own `comment`. Collect all
   non-empty comments for a given week+role bucket into the `comments`
   array, preserving chronological order (sort by `day` before grouping so
   comment order reflects the actual sequence of work, not CSV row order).

3. **Name lookup, not passthrough.** `wbs1_name`/`wbs2_name`/`wbs3_name` map
   directly to `name` at each level of `blob-input-schema.json` — straight
   passthrough, no derivation needed, **provided the export includes these
   columns**. If a future export is missing them, do not silently fall back
   to using the WBS code as the name — fail loudly, since the LLM's
   `rationale` output and the final plan both depend on real phase/task
   names, not codes.

4. **Leaf determination — three cases, and one of them isn't mutually
   exclusive with the others.**
   - `wbs2` and `wbs3` both blank: hours are charged directly to the
     **project**, with no phase assigned. In practice this is commonly
     pre-award/pursuit work logged before a WBS phase breakdown exists yet
     (e.g. Sunridge Public Library in the samples: two pursuit-debrief rows
     with blank `wbs2` sit alongside a later Schematic Design phase). Group
     these rows into `project.weeklyHours`.
   - `wbs2` populated, `wbs3` blank: hours are charged directly to the
     **phase** — that phase's `weeklyHours` is populated, its `tasks` array
     stays empty.
   - `wbs2` and `wbs3` both populated: the **task** is the leaf — the
     parent phase's `weeklyHours` stays empty, hours belong to the task.

   The phase/task case is a strict either/or: once a phase has any task rows,
   all of its hours belong under tasks, never split between the two. The
   project/phase case is **not** that kind of either/or — `project.weeklyHours`
   and a populated `phases` array can and do coexist, because "no phase
   assigned yet" and "phases exist" describe different hours, not two
   representations of the same hours. Don't force unphased rows into a
   placeholder phase to avoid populating both.

5. **`firstChargedWeek`/`lastChargedWeek` are OBSERVED charge activity, not
   a planned schedule — and they're named that way on purpose.** An earlier
   version of this pipeline derived absolute `startDate`/`endDate` from
   `min(day)`/`max(day)` per WBS grouping, which conflated when work was
   *actually charged* with the phase/task's *planned* schedule — a delayed
   kickoff shifts actual dates later than planned ones, and the two can
   differ meaningfully. Going relative resolves this by construction rather
   than by disclaimer: `firstChargedWeek`/`lastChargedWeek` are explicitly
   named as observed activity, and there is no `startDate`/`endDate` field
   left to mis-derive or misread as a plan. `weekZeroStart` (a calendar date,
   kept only for human traceability) has the same caveat — never use it to
   align one project against another.

   A planned-schedule source (e.g. a Vantagepoint project/phase master
   export) is still not available. If one is added later, planned dates
   should be a new, separately-named field (e.g. `plannedStartWeek`) additive
   to this shape — not a reinterpretation of `firstChargedWeek`/
   `lastChargedWeek`, which must keep meaning what their names say.

6. **Validate WBS codes against a master list where possible.** If a
   project/phase/task master list is available (see point 5), also use it
   to validate that `wbs1`/`wbs2`/`wbs3` codes appearing in the timesheet
   actually correspond to real, current WBS entries — a typo'd or
   deprecated code in raw timesheet data should be caught during
   aggregation, not passed through into the LLM input.

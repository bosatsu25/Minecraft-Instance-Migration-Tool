# Migration Report

Phase 4.4 presents the result of one migration session as a read-only projection. The report is a view
of existing evidence, not a new source of truth and not an authorization token for Execute or Rollback.

## Evidence and outcomes

Application accepts a transient snapshot of the current workflow evidence:

- Migration Preview action counts;
- Backup plan and typed backup result;
- typed execution and verification result;
- Recovery diagnosis, when one exists;
- typed rollback attempt result, when one exists.

That transient input may reference existing in-memory results containing private locations. It is not a
persistence model. The projector copies only the typed outcomes and counts listed below into the report.

The projector can return Completed, Cancelled, Blocked, RecoveryRequired, Recovered, GuardRejected,
Failed, or Uncertain. It refuses to create a report when evidence required for the claimed state is
missing or contradictory. In particular, Recovered requires all diagnosed rollback candidates to have
Applied evidence. A partial rollback cannot be presented as recovered.

Report generation has no effect on `MigrationWorkflowSession`. Failure to project or display a report
does not change a completed migration into a failed migration and does not alter recovery eligibility.

## Privacy boundary

The durable and presentation report models contain only typed outcomes and counts. They do not contain:

- source, destination, safety-workspace, backup, or journal paths;
- user or machine identifiers;
- raw exception messages;
- file contents.

The WPF adapter catches projection failures and shows bounded text without exception details. A root
change or a new Preview clears the previous report from the UI.

## Persistence decision

Phase 4.4 keeps the report in memory and does not write or export a report file. This avoids automatic
writes to user-selected locations and avoids silently choosing collision or overwrite behavior.

Report-file export is outside the completed v1.0.0 member-edition scope. It is not a remaining release
task. If separately requested, an export feature must cross an Application port and define all of the
following before an Infrastructure adapter is added:

- user-initiated destination selection or an explicitly owned report directory;
- a versioned, path-free schema;
- malformed/unknown-schema rejection;
- create-new or atomic-replace semantics;
- partial-write recovery;
- tests proving that private paths and raw exceptions cannot be serialized.

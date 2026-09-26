# Phase 1: read-only Instance Inspector

## Design

The input is a user-selected, fully qualified local Windows directory. The result contains
root state and immutable observations for eleven known direct children; it contains no full
paths, migration decisions, confidence score, or compatibility claim.

- Domain: actual entry state, expected kind, immutable child observations, and evidence summary.
- Application: known target catalog, read-only observation port/session, and cancellable use case.
- Infrastructure: open existing local path components without following reparse points; query
  metadata only. Resolve single names relative to retained parent handles to avoid path races.
- App: one small ViewModel, folder selection, Inspect/Cancel, root state and observation table.

To enforce the no-follow rule on Windows, use a narrow native metadata adapter with BCL
SafeFileHandle lifetime management. Ordinary path-based attribute checks alone have a race
between checking a parent and using it. No handles request data-write access or create files.
Inputs using UNC/device syntax, relative paths, parent traversal, or mapped network drives
are rejected before inspection. Inaccessible/unknown observations never become Missing.

## Implementation and verification

- [x] Establish Domain/Application contracts and behavior tests, including partial failure and cancellation.
- [x] Implement the bounded Windows adapter and real disposable-fixture integration tests.
- [x] Prove root/child/ancestor links are not followed and state is unchanged by real inspection.
- [x] Temporarily violate the read-only guard, observe RED, remove violation, observe GREEN.
- [x] Connect the verified core to a minimal WPF screen; test meaningful ViewModel behavior.
- [x] Update affected docs; build Release with zero warnings/errors; run all tests and formatter.

The final commit, PR and hosted CI evidence are recorded in the PR and delivery report.
Local verification: 72 tests passed, none skipped; Release build had zero warnings/errors;
format verification passed. Interactive UI smoke could not be completed because a persistent
target window was not available; process startup returned exit code zero, which alone does
not establish working folder selection or rendering.

## Review focus

Ancestor links and concurrent path replacement; invalid/device/network paths; incomplete observations
versus absence; cancellation without stale UI results; read-only fixture integrity and safe cleanup.

## Boundaries

No recursion, content reading/parsing, sizes/hashes, writes, migration rules, planning, launchers,
compatibility analysis, or Phase 2 work. OS metadata calls are synchronous; cancellation is checked
between calls. The observations describe multiple reads, not an atomic filesystem snapshot.
PR merge remains a human gate.

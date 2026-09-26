# Architecture

## Phase 0 boundaries

| Project | Responsibility | Allowed product references |
| --- | --- | --- |
| Domain | Pure migration policies and value models, in later phases | None |
| Application | Use cases, orchestration, ports for external effects | Domain |
| Infrastructure | Filesystem and other adapters implementing Application ports | Application, Domain |
| App | WPF views, MVVM view models, composition | Application, Infrastructure |

Dependencies point inward. App may construct Infrastructure at the composition root;
view models depend on Application contracts, not concrete adapters. Infrastructure must
never be referenced by Application/Domain. Test projects are not production dependencies.

Only App targets `net10.0-windows` and enables WPF. Other projects target `net10.0`.
Domain, Application, and Infrastructure intentionally contain no product types yet.
Empty assemblies establish reference boundaries without inventing a migration API.

## MVVM

Views own layout and UI-only behavior. Future view models translate UI commands into
Application requests and expose presentation state. Rules, exclusions, plan validation,
backup policy, and verification decisions must remain outside views/view models.
The static shell has no state or commands, so there is no artificial view model, service
container, or MVVM framework. Add those only when a concrete use case warrants them.

## Migration Engine direction (not implemented)

Develop one node at a time, starting with a read-only Inspector.

```text
Inspect → Plan → Preview / Dry Run → Backup → Execute → Verify → Report
                                                   Verify failure
                                                          ↓
                                                      Diagnose
                                                          ↓
                                                      Rollback
                                                          ↓
                                                       Report
```

- **Inspect:** Infrastructure reads explicitly selected instance roots; Application returns
  observations. No destination creation or modification. Unknown/inaccessible entries are
  reported as incomplete inspection, not silently treated as absent.
- **Plan:** Domain policies produce explicit relative source/destination mappings, exclusions,
  conflict decisions, and intended effects from observations. Separate immutable intent from execution state.
- **Preview / Dry Run:** Present that same plan without writes, including exclusions, conflicts,
  size estimates, uncertainty, and required confirmations. Do not compute a different implicit plan at execution.
- **Backup:** Establish and verify recoverable destination state before modifying it. Inability
  to establish a backup must stop execution. Define crash recovery and manifest format before implementing writes.
- **Execute:** Revalidate roots and source/destination state against the reviewed plan.
  Reject stale plans, unsafe paths, or changed collision assumptions; cancellation and partial writes need explicit outcomes.
- **Verify:** Compare actual outcomes against the plan using defined evidence (such as content
  hashes where required), independently of an executor's success flag.
- **Diagnose / Rollback:** Preserve failure evidence without private payloads. Attempt recovery
  using verified backup information and an execution journal. Rollback may fail and must
  never be reported as successful merely because it was attempted. Do not blindly undo later user edits.
- **Report:** Distinguish succeeded, failed, cancelled, partially changed, rolled back, and
  recovery-required outcomes. Define bounded, redacted diagnostics before persisting any report.

Application will own workflow transitions, cancellation, failure handling, and orchestration.
Domain will own deterministic rules and plan invariants. Infrastructure will own actual IO.
Define ports only when the first use case requires them; this document is not an API contract.

## Filesystem safety requirements for later phases

Reject identical/nested roots and traversal outside controlled roots. Account for Windows
case rules, junctions/reparse points, symlinks, locks, long paths, permission failures, and
time-of-check/time-of-use changes. Normalizing a string alone is not containment proof.
No silent overwrites; conflict policy belongs in the reviewed plan. Future archive extraction
must prevent Zip Slip. Source data is read-only unless a separate explicit request authorizes otherwise.

Tests must pin down behavior before high-risk writes are implemented. Integration tests use
owned temporary directories, never real instances. No network adapters are in scope.

## Enforcement and limits

Architecture tests check evaluated project references and UI properties in Debug/Release,
plus actual assembly metadata and a focused list of direct Domain filesystem types.
They inspect metadata without loading product assemblies dynamically.

These are regression guards, not a sandbox or full static analyzer: indirect third-party IO,
reflection, native calls, source-level business-rule placement, custom MSBuild target side
effects, and every possible UI framework are not exhaustively detected. Review dependency
changes and adapter behavior explicitly. See [testing](testing.md) and [ADR 0001](adr/0001-technology-and-architecture.md).

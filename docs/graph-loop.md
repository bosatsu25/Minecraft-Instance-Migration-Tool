# Evidence-driven Graph Loop

The development loop is distinct from the product's migration workflow.

```text
DISCOVER → PLAN → IMPLEMENT → VERIFY
                                ├─ PASS → completion assessment
                                └─ FAIL → DIAGNOSE → FIX → VERIFY
```

## States and decisions

| State | Required outcome |
| --- | --- |
| DISCOVER | Actual branch/status, relevant code and requirements, environment limitations |
| PLAN | Small scoped change, affected boundaries, acceptance criteria, verification commands |
| IMPLEMENT | Reviewable changes within authorization; tests for changed contracts |
| VERIFY | Commands, exit codes, results, changed-file review, and remaining gaps |
| DIAGNOSE | Reproduction and evidence-based cause; distinguish product defects from environment failures |
| FIX | Small cause-directed correction and regression test where appropriate |
| Completion assessment | Compare acceptance criteria with evidence; choose COMPLETE, RETRY, or ESCALATE |

- **COMPLETE:** All applicable acceptance criteria have supporting evidence; report any
  agreed exclusions precisely. A configured CI workflow is not evidence of a successful hosted run.
- **RETRY:** A fix or additional deterministic check can resolve a known local gap.
- **ESCALATE:** Required information/permission is missing, an external dependency blocks
  verification, or the remaining action crosses a human gate. Explain the exact blocker;
  continue unrelated authorized work. Do not retry unchanged failures indefinitely.

## Deterministic before probabilistic

Use the compiler for compilation, the test runner for tests, the formatter for formatting,
`git diff` for changed content, and `git status` for repository state. Human/AI review
interprets requirements, design, and risk; it cannot replace these checks or invent PASS.

For an initial uncommitted repository, `git diff` alone omits untracked files. Inventory
and read those files explicitly, or compare them against an empty file with `git diff --no-index`.
Do not stage secrets or generated output just to obtain a diff.

## Evidence before decision

Record the relevant command, configuration, exit code, test counts, failures, and remaining
limitations in the work report. Include what was not run. Keep records concise; never copy
credentials, private instance paths, account identifiers, or user payloads into repository docs.
A local PASS, hosted CI PASS, and manual UI observation are different kinds of evidence.

## Fix and re-verify

Observe → diagnose → fix → rerun the failed check. Then run checks for affected neighbors.
A failure caused by the requested change must be fixed within scope. Do not hide failures by
disabling tests, weakening assertions, suppressing warnings, or changing the target framework.
If a check cannot run, report it as unverified, not passed.

## Minimal sufficient verification

- Prose-only change: links, requirements alignment, diff/whitespace review.
- Ordinary code change: build, relevant tests, format verification; broaden when shared behavior changes.
- Foundation/build/dependency/boundary change: restore, full Release solution build, all tests,
  format verification, and git review.
- MigrationRule, Backup, Rollback, Verification, or destructive operations: deterministic unit
  tests plus integration, failure-path, and regression tests for affected safety guarantees.

Phase 0's full verification commands are in [testing](testing.md).
Do not require every document or every test for every tiny change.

## Autonomy and human gates

Authorized local investigation, editing, builds, testing, formatting, static analysis, failure
diagnosis, and documentation proceed without repeated confirmation.
Merge, release, deployment, destructive remote operations, external data upload,
secret/credential handling, and irreversible history rewriting require a human gate.
Git commits, branches, pushes, and remotes follow the explicit authorization rule in AGENTS.md.

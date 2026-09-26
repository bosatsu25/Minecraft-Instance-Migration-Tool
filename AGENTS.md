# Repository working agreements

- Scope work to the requested phase. Phase 0 contains only the foundation and a static WPF shell.
- Before editing, inspect branch, git status, and relevant files. Preserve existing work.
- Do not commit, push, create/delete branches, or change remotes without explicit authorization.
- Follow the evidence-driven Graph Loop: DISCOVER → PLAN → IMPLEMENT → VERIFY.
  On failure: DIAGNOSE → FIX → VERIFY. Assess completion only against actual evidence.
- Deterministic before probabilistic: use compiler, test runner, formatter, git diff, and git status
  for facts they can establish. Never infer PASS.
- Choose minimal sufficient verification for the affected scope. Migration rules, backup,
  rollback, verification, and destructive filesystem operations are high risk.
- Keep Domain independent of UI, filesystem implementations, and outer layers.
  Application must not depend on UI or Infrastructure. Keep business rules out of App.
- Use nullable C#, the configured .NET SDK, and ordinary non-preview language features.
  Do not suppress warnings without a specific, documented justification.
- Avoid unnecessary dependencies, speculative interfaces, and placeholder tests.
- Add/update meaningful tests when behavior or public contracts change.
- No default-on file-changing automation, telemetry, network calls, or external services.
- Treat user paths as untrusted. Future writes need containment and explicit collision handling;
  archive extraction must prevent Zip Slip. Do not log private user data, paths, or identifiers.
- Do not manually edit or stage generated outputs, caches, local tools, IDE/agent state,
  logs, credentials, secrets, or machine-specific paths. Build/test generated output is expected.
- Proceed autonomously with authorized local edits, builds, tests, formatting, diagnosis,
  and documentation. Human gates: merge, release, deployment, destructive remote operations,
  external data upload, credential handling, and irreversible history rewriting.
- Report changed files, actual commands/results, unresolved risks, branch/commits,
  and a suggested commit message. Never merge a PR as part of Phase 0.

## Read documentation when relevant

| Change or decision | Read |
| --- | --- |
| Architecture or layer boundaries | [architecture](docs/architecture.md), [ADR 0001](docs/adr/0001-technology-and-architecture.md) |
| Migration selection or exclusion rules | [migration rules](docs/migration-rules.md) |
| Tests, CI, verification scope | [testing](docs/testing.md) |
| Graph Loop, failed checks, completion decisions | [Graph Loop](docs/graph-loop.md) |

Read only the documents relevant to the current work; there is no read-everything prerequisite.

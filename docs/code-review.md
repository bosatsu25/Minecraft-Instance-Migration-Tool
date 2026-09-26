# AI code review

OpenCodeReview is an advisory PR reviewer. It complements deterministic verification; it does
not replace the compiler, tests, formatting checks, architecture guards, UI smoke tests, or
human merge decisions.

## Upstream pin

The workflow uses Alibaba OpenCodeReview v1.12.9. The GitHub Action is pinned to immutable
commit `bccbc15f785269400735d5255540c231e6c02b6d`, the commit referenced by the v1.12.9
annotated tag when this integration was added. The OCR CLI is also pinned with
`ocr_version: 1.12.9` so the wrapper and CLI do not silently drift independently.

Review upstream release notes and the action diff before changing either pin.

## Rollout

The workflow is intentionally opt-in. It runs only when the repository variable
`OCR_ENABLED` is exactly `true`.

Required repository configuration:

| Kind | Name | Purpose |
| --- | --- | --- |
| Secret | `OCR_LLM_URL` | LLM API endpoint |
| Secret | `OCR_LLM_AUTH_TOKEN` | LLM credential |
| Variable | `OCR_LLM_MODEL` | Provider model identifier |
| Variable | `OCR_LLM_USE_ANTHROPIC` | `true` for Anthropic protocol; otherwise OpenAI-compatible |
| Variable | `OCR_ENABLED` | Explicit rollout switch |

Never commit credential values. ChatGPT subscriptions and other consumer subscriptions are not
API credentials for this workflow.

If OCR is disabled or unconfigured, normal CI remains authoritative and independent.

## Security model

The workflow uses `pull_request_target` so repository secrets remain available for PR reviews,
including fork PRs. That event is safe here only because the pinned upstream action reviews Git
objects/diffs and does not execute the PR's code. Keep that property when upgrading.

Permissions are limited to:

- `contents: read`
- `pull-requests: write`

Do not add `contents: write`, automatic fixes, commits, releases, or auto-merge to the reviewer.
The current configuration leaves outdated-thread resolution disabled because enabling mutation
would require broader permissions.

## Review policy

Initial rollout favors signal over volume:

- Japanese output.
- Medium review effort.
- Sticky summary enabled.
- Low-severity findings and style/documentation findings are routed to the PR summary.
- No automatic fixes.
- No automatic thread resolution.
- No merge blocking solely because the reviewer is unavailable or reports findings.

A finding is a hypothesis to investigate. Classify it as a real defect, an uncertainty needing
reproduction, or a false positive. Fix real defects and rerun deterministic checks before relying
on a re-review.

"No findings" is not proof of correctness.

## Graph Loop integration

The intended review path is:

```text
DISCOVER -> PLAN -> IMPLEMENT -> VERIFY
                              |
                              +-- FAIL -> DIAGNOSE -> FIX -> VERIFY
                              |
                              +-- PASS -> AI REVIEW (when enabled)
                                           |
                                           +-- valid finding -> FIX -> VERIFY
                                           +-- false positive -> document/dismiss
                                           +-- no finding -> completion assessment
```

Deterministic evidence comes first. See [Graph Loop](graph-loop.md).

## Operational verification

A committed workflow is not evidence that OCR is operational. Call the integration operational
only after an actual PR run has:

1. entered the OpenCodeReview job with `OCR_ENABLED=true`,
2. validated the configured endpoint, token, and model,
3. invoked the pinned action and OCR CLI,
4. produced review output, and
5. posted or updated the expected PR review summary/comments.

Until then, report the integration as installed but not runtime-verified.

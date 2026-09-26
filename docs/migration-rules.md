# Future migration rules

**The legacy compatibility/preset rules remain design inventory.**
Phase 1 observes the eleven known names. Phase 2 adds explicit user-selection planning for those
same names, but does not implement Recommended presets, nested exclusions, compatibility rules,
or collision resolution. The legacy behavior below is supplied by the product brief, not inferred
from an inspected legacy codebase.

| Instance-relative candidate | Legacy intent | Recommended preset direction |
| --- | --- | --- |
| `options.txt` | Game/user settings | Candidate |
| `config/` | Mod/user configuration | Candidate, with exclusions |
| `resourcepacks/` | Resource packs | Candidate |
| `shaderpacks/` | Shader packs | Candidate |
| `schematics/` | Schematics | Candidate |
| `saves/` | Worlds | OFF by default; explicit opt-in |
| `screenshots/` | Screenshots | OFF by default; explicit opt-in |
| `XaeroWaypoints/` | Xaero waypoint data | Candidate |
| `XaeroWorldMap/` | Xaero map data | Candidate |
| `itemscroller/` | Item Scroller user data | Candidate |
| `g4mespeed/` | g4mespeed user data | Candidate |

Phase 2 treats a selected source that is present with the expected kind and a missing destination
as `ReadyToCopy`. Missing sources are explicit no-ops. Existing destinations remain unresolved
conflicts; unsafe/unknown observations block the plan. This is intent only and performs no IO.

The legacy exclusion is **`hanemod-client.json` must not be migrated**.
Before implementing it, explicitly resolve whether matching is a relative path, basename
at any depth, and/or case-insensitive Windows matching. Tests must cover each chosen scope.
Until then, do not claim that a real migration safely enforces this exclusion.

“Candidate” is not automatic inclusion or a compatibility guarantee. Phase 2 requires explicit
selection. A future Recommended preset needs explicit acceptance criteria, preview visibility,
and the two OFF defaults above.
Exclusions must take precedence over inclusion and presets.

## Required decisions before rule implementation

- Missing candidates, unreadable entries, empty directories, and partial inspection.
- Collision policy: skip/replace/merge semantics and their backup requirements; no silent overwrite.
- Nested exclusions, case differences, duplicate destinations, and deterministic rule ordering.
- Source/destination containment and overlap, junctions/symlinks, stale plans, and concurrent changes.
- Large worlds/screenshots, free space, cancellation, and bounded diagnostics.
- Cross-version configuration and mod-data incompatibility: copying does not prove compatibility.

Rules belong in Domain and must be testable without UI or filesystem access.
Application applies them to Inspector observations; Infrastructure performs authorized IO.
See [architecture](architecture.md) for the full planned workflow and [testing](testing.md)
for the high-risk verification requirements.

# Botology UI/UX Recommendations

**Review date:** 2026-08-18  
**Scope:** UI code review only; no runtime behaviour or implementation changes are included in this document.

## Product goal

Find plugin conflicts quickly, understand severity, take the correct action, and safely maintain local catalog overrides.

## Reviewed surfaces

- `botology/Windows/MainWindow.cs`
- `botology/Windows/ConfigWindow.cs`
- `botology/Windows/CatalogEditorWindow.cs`
- `botology/Windows/DtrManagerWindow.cs`

## What is already working

- The main grid combines catalog, installed/enabled/update, DTR, repository, author, and assessment data.
- Filters, column selection, live status, alerts, catalog editing, and DTR management are available.
- Master rows and local overrides have an explicit source model and destructive actions already require Ctrl.

## Prioritized recommendations

| Priority | Recommendation | Rationale and completion signal |
| --- | --- | --- |
| P0 | Default the grid to decision-critical columns. | Show Plugin, Severity, Why, Installed/Enabled, and Action first. Move downloads, API level, attribution, author, dates, source, and repository into optional columns or a row inspector. |
| P0 | Add text/icon severity, not colour alone. | Every green/yellow/red assessment needs a named state such as Compatible, Review, or Conflict and a one-sentence reason. |
| P0 | Reduce the toolbar into Primary and Tools groups. | Keep Reload, current alert count, search, and filters visible; group DTR, Catalog, Data, logs, XL commands, support, and patch notes under clear secondary menus. |
| P1 | Turn filters into removable chips. | Show all active filters above the grid with result count and Clear all, especially when `No plugins match` would otherwise hide the cause. |
| P1 | Use a row detail inspector for notes and actions. | Selecting a plugin should expose full reasoning, relationships, URLs, recommended action, and change history without forcing a very wide grid. |
| P1 | Make catalog draft state explicit. | Show Master, Local override, New local, Hidden master, Dirty, and Saved badges; preview which master ID is replaced or hidden before saving. |
| P1 | Replace Ctrl-only destructive affordances with confirmation. | Keep Ctrl shortcuts for experts, but confirm drop/hide/upload operations with affected counts and provide an undo/reload path. |
| P2 | Improve DTR reordering. | Support drag handles or clearer position controls and show when an entry is plugin-hidden versus user-hidden. |

## Suggested information hierarchy

1. Alert summary and filters
2. Compact decision grid
3. Selected-plugin detail
4. Catalog maintenance
5. DTR and diagnostics

## Validation checklist

- A new user can identify the primary action and current blocker within five seconds.
- Every disabled control has a nearby plain-language reason and, when possible, a direct corrective action.
- Healthy, warning, error, running, and disabled states remain distinguishable without colour.
- The UI remains usable at narrow window widths and common Dalamud UI scales without clipped labels or unreachable controls.
- Destructive, global, or high-impact actions identify their scope and require confirmation or provide a safe undo.
- Empty, loading, stale-data, success, partial-success, and failure states each provide an appropriate next action.
- Settings clearly identify whether they apply globally, per account, per character, per preset, or only for the current session.
- Advanced diagnostics are still reachable but do not compete with the everyday workflow.

## Recommended implementation order

1. Implement P0 items and validate the primary workflow plus blocker recovery.
2. Implement P1 information-architecture and configuration improvements.
3. Apply P2 polish, then test at multiple UI scales with both fresh and mature configurations.

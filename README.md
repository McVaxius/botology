# Botology
---

**Help fund my AI overlords' coffee addiction so they can keep generating more plugins instead of taking over the world**

[☕ Support development on Ko-fi](https://ko-fi.com/mcvaxius)

[XA and I have created some Plugins and Guides here at -> aethertek.io](https://aethertek.io/)
### Repo URL:
```
https://aethertek.io/x.json
```

---

[Join the Discord](https://discord.gg/ac6gjDvR8R)

Scroll down to "The Dumpster Fire" channel to discuss issues / suggestions for specific plugins.

## Current Status

Bootstrap scaffold created on 2026-04-07. This repo now has a buildable `Debug x64` shell with a functional compatibility grid: installed state, update availability, repo link, enable toggle, best-effort DTR toggle, ignore flags, rule detail popups, category/plugin/author filters, AI attribution, cached catalog patch notes, and a growing `plugin-repository-links.json` catalog for tracked plugins.

- Solution: `Z:\botology\botology.sln`
- Project: `Z:\botology\botology\botology.csproj`
- Commands: `/botology`, `/bottist`, `/botologist`
- Repository target: `Private`

The main window follows the approved AethertekUI mockup with native table sorting,
captured installed versions, catalog source badges, DTR visibility switches, and
assessment badges in Notes / Warnings. Saved optional columns remain available;
the grid scrolls horizontally when they exceed the window width.

The top colour/language selector and Settings share `UiAccentRgb` (default
`0x1CC9E6`) and `UiLanguage` (default `en`) through the existing configuration
save path. Custom accents retint surfaces, fields, borders and text using relative
OKLCH colours while preserving the measured light/dark hierarchy. Default teal
keeps the reference colours; status and provenance colours retain their meanings.
English, German, French, Spanish, Italian, Russian, Japanese, Korean,
and Simplified Chinese resources are embedded in the plugin. UI formatting uses
the selected locale; catalog content, plugin names, command tokens and logs
remain original. Content uses managed Segoe UI regular/semibold/bold handles
with host-managed CJK and game-symbol coverage. Font loading or glyph failures
are explicit; Windows fonts are never distributed with the plugin.

The main window maps to `Z:\!prebuild\Botology-main-v1.png`, retaining native
Dalamud chrome and adding the selectors. Settings, Catalog Editor and DTR
Manager retain their layouts with shared fonts/theme and overflow corrections.
Columns, patch notes, blocking alert, description, rule-info and appearance
popups retain native interaction and identity. The measured 1440 × 942 content
uses 72/52/64/115/44/74/52-pixel header/actions/counters/filters/table-header/
minimum-row/footer roles and 38/20/22/18/16-pixel em typography at 100%.

The existing feature harness renders supplied snapshots without constructing
the plugin or starting game services. It checks nine-language windows/popups,
size/scale/accent/state cases, font coverage, configuration compatibility and
native behavior. PNG regression baselines use channel tolerance 2; initial
reference geometry/colours are checked independently within ±2 logical pixels
and ±6 RGB. Baselines are reviewed diagnostic renders, not GPU evidence.
Game visual acceptance remains pending until mcvaxius supplies a screenshot using
the reference column set and a stated scale, and accepts the reviewed result.

Local builds require the sibling checkout at `Z:\aethertekUI`. From that folder,
run the following child process to reuse its local SDK/NuGet environment without
creating release packages:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ". .\eng\Enter-RepoEnv.ps1; dotnet build Z:\botology\botology\botology.csproj -c Debug -p:Platform=x64 -p:Use_DalamudPackager=false --disable-build-servers"
```

`AethertekUI.dll` copies beside `botology.dll`; Dalamud assemblies remain host-owned.
The existing release workflow checks out only Botology, so mcvaxius must choose how
to provide AethertekUI there before releasing this integration.

Run the existing feature/visual harness after a packaging-disabled build using
the same repository environment:

```powershell
dotnet build Z:\botology\tests\Botology.FeatureTests -c Debug -p:Use_DalamudPackager=false
dotnet run --project Z:\botology\tests\Botology.FeatureTests -c Debug --no-build
```

Normal runs never replace baselines. Set `AETHERTEKUI_CANDIDATE_DIR` to a separate
ignored artifact directory only when generating candidates, inspect the images
against the design, and deliberately adopt reviewed PNGs as a Git change.

## Plugin Concept

- Track the plugins that can overlap or fight each other.
- Default toward red when a hard conflict is present.
- Keep alerting configurable so warnings can be a toast, a popup, or a forced window open.

## Catalog notes and AI attribution

- The default-visible `AI` column marks rows whose code was identified as likely AI-written by Aetherfeed contributor and coding-pattern attribution. It is an attribution signal, not a definitive authorship finding; an unmarked row is not proven human-only.
- Attribution is frozen from Aetherfeed's `https://raw.githubusercontent.com/Aetherfeed/aetherfeed.github.io/main/public/data/plugins.json` snapshot dated 2026-07-22, SHA-256 `28e98ec13f5c2feabd9166c8a4cd3749ee42b81b6a9638175106da97ec27f7f5`. Botology does not continuously synchronize this field.
- `Patch Notes` shows the newest-first catalog release history from the last valid remote response or local cache. A failed or invalid notes fetch keeps the previous cache and does not block catalog refresh.
- The compatible `ToastOnMasterCatalogChange` setting is shown as “Toast for catalog notes affecting installed plugins.” A release is evaluated once and only produces a toast when one of its affected catalog rows matches an installed plugin; disabled plugins still count as installed.

Compact mode is available from the main header?s **C** checkbox and in Settings. `UiCompact` defaults to false, uses the existing save path, and shares reduced padding and control/card/row density across windows. Body text remains readable; optional columns and saved widths are preserved.

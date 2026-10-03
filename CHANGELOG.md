# Changelog

## 2026-10-02

- Colour selections now retint the whole UI using relative OKLCH colours for surfaces, fields, borders, foregrounds, panel gradients and neutral badges. Measured lightness stays stable for dark/pale/grey selections; default teal and semantic status colours are preserved.
- Fixed live UI startup by merging Dalamud's bundled CJK font for the language selector and selected locale. Neutral language codes previously caused Windows font merging to do nothing and blocked the UI on missing glyphs. Font status windows now keep a readable width.
- Added measured appearance/geometry roles, slate surfaces, Segoe content typography, native vector branding, explicit switch tracks, wrapping controls and a reserved footer. Native Dalamud chrome remains in place.
- Added shared colour and nine-language selectors in the header and Settings, two compatible configuration preferences, embedded translations and locale formatting. Cached managed fonts check loading/glyph coverage and atlas rebuilds; appearance changes apply next frame and custom RGB saves on edit completion.
- Extended the existing offline feature harness with snapshot-based native window/popup scenarios, reference bounds/colour checks and deterministic RGBA regression comparisons. Diagnostic evidence remains separate from pending game screenshot acceptance.
- Kept wrapped popup widths stable after opening and checked that native button interactions actually render each popup without collapsed bounds.
- Adopted the local AethertekUI library for the approved main-window design: cyan accent, compact header and action toolbar, assessment counters, category dropdown, searches, filters, sortable plugin grid, and launcher footer outside the scrolling table.
- Preserved saved column choices and existing actions, DTR visibility paths, notifications, descriptions, rule details, and window placement. Installed cells show captured versions; updates show availability; source badges show actual catalog provenance; assessment badges remain in Notes / Warnings.
- Added local project references and a theme scope around the complete window lifecycle. Versions remain Botology 0.3.0.1 and AethertekUI 0.3.0; release CI will need separate library provisioning before publication.

## 2026-10-01

- Build only the plugin project in GitHub Actions so test and regression projects do not block production artifacts.

## 2026-07-22

- Added optional `IsAiAttributed` catalog support through master/local storage, overlay equivalence, editor/export, Python review validation, bundled data, and compiled fallbacks. The default-visible `AI` column uses the frozen 2026-07-22 Aetherfeed attribution snapshot and describes the signal as likely, not definitive.
- Added schema-v1 catalog patch notes with a cached, failure-isolated fetch path and a `Patch Notes` modal in the main window.
- Replaced the generic master-catalog-change toast with one-time release evaluation limited to affected installed plugins, including installed-but-disabled plugins, while preserving the existing configuration key.
- Added focused Python catalog validation and a deterministic C# feature harness for notification and changelog-cache behavior.

## 2026-04-09

- Removed the throwaway `MISSING JSON` popup, added an `Author` search field, expanded `plugin-repository-links.json` with the user-supplied installed plugin list under `ZZUNCATEGORIZED`, and added the explicitly-missed `VIWI` / `Chilled Leves` feed rows.

## 2026-03-25

- Bootstrapped the `Eyes of Gohd` repository shell.
- Added the Dalamud project, solution, plugin manifest, windows, and DTR/Ko-fi baseline.
- Added icon assets at `images\iconHQ.png` and `images\icon.png`.
- Added the initial import guide and README.

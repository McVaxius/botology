2026-10-09 - Tight compact plugin and DTR grids (I503/I509)


- Apply the approved adjacent-row table style to compact plugin and DTR lists. Let the tallest retained editor set plugin-row height while preserving sorting, column identities, links, visibility actions and scrolling.
- Refresh the existing native UI fixture with the actual embedded icon and balanced failure cleanup; keep candidate renders separate from approved baselines and game acceptance.

2026-10-09 - Separate XA Slave log-tools shortcut (I512)

- Add Open XA Slave log tools beside the existing manual support exporter when XA Slave is loaded. The new action opens Utility > XA Mods only; preserve the Copy / ZIP button, its handler and cap warning. No automatic cleanup, provider loading or settings changes.

2026-10-09 - Manual Dalamud support log export (I506)

- Add Copy / ZIP Dalamud log and Open Export Folder to the existing settings/support interface. At 100 MiB or above, warn that logging may have stopped and recent activity may be missing; require another explicit click to export. Exports stay local and can be shared or removed manually. Preserve saved settings and release versions.

2026-10-08 - Dedicated Window appearance settings (I505)

- Move colour, language, compact mode and transparency controls into their own settings tab or sidebar page. Retain the existing controls, native IDs, saved preferences and actions; keep normal settings visible without an appearance block above them. Versions and client configuration are unchanged. Local build checks and game visual acceptance are recorded separately in the selected task checkpoint.

2026-10-08 - GitHub Actions shared-library repair

- Build against published AethertekUI main so current shared APIs are available. Retain repository-specific read-only SSH deploy keys, which do not expire, and disabled credential persistence. Publish library APIs before consumer changes.

# Changelog

## Unreleased - Hindi font availability and recovery

- Treat the Hindi language-menu caption as optional while retaining mandatory selected/English catalogue checks. Refresh the stable Hindi option's availability with the existing font generation; unavailable captions use disabled ASCII `Hindi (unavailable)` without blocking ordinary languages.
- Keep font-failure status readable in ASCII and offer an explicit Use English action for selected Hindi through the existing configuration save route. Retain atlas roles, merges, dimensions and native control IDs.
- Source integration is complete; compilation, native availability/recovery checks and Linux/Wine acceptance remain pending.

## Unreleased - Original plugin images and UI guidance

- Replace Main's drawn emblem with the existing embedded plugin icon and add original-colour images to Main titles, including collapsed windows. Retain existing body geometry, title text, native actions and saved window placement; borrowed host textures keep aspect ratio and a blank reservation while unavailable.
- Clarify shared appearance controls and this plugin's existing automation/setup ownership in the README.
- Source integration is complete; compilation, native image/title/control checks and game acceptance remain pending.

## 2026-10-07 - Managed CJK font atlas

- Merge one bundled CJK face per font role, selecting the active language's regional forms. Set both managed atlas dimensions to 4096 on every rebuild; preserve font heights, required glyph ranges, symbol merges and host-language coverage.
- Current compilation and guarded production callback/rebuild checks pass, together with bounded native glyph checks for the checked text. Managed-host readiness, complete displayed glyph coverage, language/scale host rebuilds and game/GPU acceptance remain unverified.

## 2026-10-07 - Native titlebar shortcuts

- Add Settings and the existing assessment-alert enable toggle to the native main titlebar, retaining every body control and the current configuration/save path.
- Reserve title and button space before window motion and keep custom title text clear of native buttons.
- Validate the current Debug/x64 build through the unchanged launcher: zero warnings and errors. Focused English installed-host checks pass 430 assertions and 16 native pointer presses across both densities/scales and settled collapsed/expanded windows. Settings invokes the supplied existing UI contract; Enabled uses the actual retained save/feedback handler. Actual catalog refresh, managed-icon, GPU and game acceptance remain separate.

## 2026-10-07 - Community invite

- Update the existing Discord community button and current README link to https://discord.gg/ac6gjDvR8R.

## 2026-10-06 - Actions shared-library revision

- Pin the existing AethertekUI checkout to published commit 6c193cf06ac67f954c549cafc2033ac0efdd630a so fresh builds receive the Hindi shaping APIs required by this consumer. Preserve existing credentials, build/package paths and release behavior; hosted execution is verified separately against each published commit.

## 2026-10-06 - Hindi UI integration

- Add the complete 207-entry Hindi catalog through the existing resource and saved-language paths. Use the shared Windows shaping host for normal and font-status windows, translated measurement, captions, tooltips, selectors, and retained single-line/multiline editors. Preserve native control IDs, Unicode values, automation and other saved preferences.
- Validate shaped text in each original font role before checking the retained atlas for other scripts. The unchanged launcher and feature project build with zero warnings/errors. Focused actual-product checks cover all 15 resource/save paths, retained Hindi controls and seven window scenes; the two actual catalog multiline editors preserve Hindi, CRLF and supplementary values in an additional focused render. Game/GPU/actual IME acceptance remains separate.

## 2026-10-06 - Window appearance and transparency

- Move colour configuration into the existing Settings Window appearance section, retaining compact/language access there. Independently hide or show the main-window compact and language controls, both visible by default. Add the main Transparency toggle without changing automation actions or native control identities.
- Persist full-window opacity through the existing configuration: 100% normal opacity, automatic fade enabled, 50% unfocused opacity after 10 seconds. Clamp opacity to 10-100% and delay to 0-3600 seconds. Apply one shared opacity pass per owner after native drawing and motion restoration, including collapsed and font-status windows; retain native chrome, image alpha and owned child/pop-up content.
- Translate the eight new appearance labels in all 14 existing catalogs. Current-version compilation, focused persistence/focus checks and game acceptance remain pending for this source change.

## 2026-10-05 - Rounded window chrome and native minimize (source adoption)

- Adopt per-window rounded chrome and animated native minimize/restore through the shared PositionedWindow lifecycle, including font status. Preserve control identities, layout, saved geometry, actions and existing NoCollapse behavior.
- Compilation and offline native-control checks passed. Managed-host and game acceptance of rounded chrome and animated minimize/restore remain pending.

## 2026-10-04 - Additional UI languages

- Append Vietnamese, Brazilian Portuguese, Indonesian, Polish and Turkish to the original language choices. Translate every existing catalog entry through the existing UI resource route, preserving the original locale order, native controls, stored settings, external data and configuration save path.

## 2026-10-03 - Compact appearance

- Restore the current assembly version in the main title bar without changing its native ID. Measure translated fields, numeric step controls, filter previews and badges; reflow complete controls and allow horizontal scrolling while retaining saved column widths and actions.
- Reconcile the compact action/footer heights, counter proportions, region gaps, filter insets, field widths and initial column proportions with the approved compact reference. Retain full-height filter fields and 64-pixel minimum rows, measure translated counter labels/counts, darken compact surfaces, center captured grid values and use the reference's inline assessment indicator without changing native controls, status meaning or saved column widths.
- Restore for the same configuration and x64 platform used by the build, both in Actions and the direct plugin batch launcher. Preserve caller build arguments and stop on restore failure.
- Added the approved compact presentation with a header C checkbox and localized Compact mode setting, saved through existing configuration without changing its version. Corrected native characters in the new compact label across all nine resources.
- Shared density across all windows; retained readable body typography, native actions, saved widths and independent status colours.
- Correct the verified Korean CJK face selection, merge host symbol coverage and check English fallback/support glyphs alongside the selected locale. Missing-glyph diagnostics identify their font role.

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

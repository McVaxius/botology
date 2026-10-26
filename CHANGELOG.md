2026-10-26 - 3.1.0.1 - Approved material styles and colour controls

- Add Glassmorphism, Spatial and Cyberpunk across the installer, Settings, DTR Manager, Catalog Editor and native supporting surfaces. Preserve Auto, Picker, LCARS, existing actions, IDs, font roles, density and opacity.
- Add independent palettes and resets, LCARS Background/three-bar colours, and Glass animation, speed and separately resettable wave colours through the existing appearance settings. Translate the controls in all fifteen catalogs.
- Update the existing CI library pin to the published AethertekUI revision consumed by this integration. Preserve the current installer and Update all behavior.

2026-10-24 - Manual Update all (local implementation)

- Add an explicit Update all action on Updates for every currently eligible installed plugin, including entries hidden by search and filters. Retain the individual Update action, native source and testing-channel selection; assess updates against the native selected channel's API while preserving stable Install eligibility.
- Await each native update serially under the existing installer operation gate. Recheck host, safe-mode, development, banned, API and repository eligibility before each dispatch; stop on the first failure and show completed counts and the affected source. Translate the action and results in all 15 locales.
- Preserve version 3.0.0.4. No background update scheduling or saved update preference is added.
- Validation: Debug/x64 product and existing feature harness build cleanly. Installer core and focused native Update all checks pass, including normal/LCARS interactions, seven selected-channel API/fallback cases, stale eligibility and all fifteen resource/placeholder catalogs. In-game updater acceptance remains pending; no client updates or baseline changes were performed.

2026-10-23 - LCARS appearance (local implementation)

- Add LCARS after Auto and Picker through the existing shared appearance configuration, retaining Picker RGB values and the automatic palette. Use segmented headers, elbow rails, alternating installer frames and outlined actions across Main, Settings, plugin details and confirmations, DTR Manager and Catalog Editor.
- Share one default-muted UI sound preference between Appearance and native speaker shortcuts. Only LCARS activations request the owned optional tones; muting, leaving LCARS and unload stop or release owned playback. Translate the controls and help in all 15 locales.
- Pin consumer CI to the published AethertekUI 0.3.0 baseline. Preserve version 3.0.0.3, native IDs, action eligibility and the existing manual per-plugin updater.

2026-10-22 - Compatibility feedback and hover descriptions (local implementation)

- Include each alert's actual assessment reason and missing dependency names while retaining ignored-row filtering, severity, total counts and the four-row summary limit. Format notification, status and titlebar reasons with the existing UI language resources.
- Show wrapped plugin descriptions on hover over the existing info icon. Its display-only target cannot retain button focus or an outline after a click; descriptions disappear when the pointer leaves without a sticky popup or capturing keyboard focus.
- Preserve exact normalized runtime-name matching and the host's loaded/disabled state. An old plugin directory alone is not evidence that the plugin is loaded.

2026-10-20 - Independent appearance colours (local implementation)

- Add Auto and Picker to Colour in the native titlebar and existing Settings. Auto keeps the existing palette and custom accent; Picker retains independent Background, Controls and Text choices through the existing global save path.
- Keep cyan Controls independent of the chosen Background when drawing decorative panels and badges. Saved Picker choices remain available after returning to Auto.

2026-10-20 - Uniform Discover cards (local implementation)

- Use one shared card size and aligned media, title, metadata and action areas across the grid. Long titles and preview loading/failure no longer resize individual cards.
- Display the real plugin icon prominently in the same media area when a screenshot is absent, enlarging small icons proportionally while retaining complete artwork. Keep every title below its media and move subordinate missing-preview feedback to the existing tooltip.
- Preserve media geometry and typography between Regular and Compact; Compact tightens surrounding spacing and control padding. Retain responsive columns, visible compatibility, source favorites, actions and browsing anchors.
- Local verification passed 39 native installer cases across 15 locales and eight title-hook contexts. Core/discovery/media checks passed; the unchanged performance budgets passed on rerun after an initial timing failure. Game appearance and timing remain user-operated checks.

2026-10-19 - Installer card and media proportions (local implementation)

- Derive Discover spacing and controls from current text and container proportions. Keep the media canvas and screenshot/icon sizing stable when switching Regular and Compact; Compact tightens surrounding padding and gaps without changing grid breakpoints, artwork size or typography. Fit complete publisher screenshots without the extra inset.
- Combine the real icon, title and subordinate media feedback in missing-preview identity headers. Keep Clear filter beside result tools when space permits and make the collapsed category strip slimmer while retaining all three expandable tag tracks.
- Size pinned-source rows and eligibility badge icons from current text/control metrics so they follow the same relative layout and density rules; reserve matching badge geometry in the table.
- Measure card content against the native rounded allocation, keeping full-width badges and actions inside the card's inset at fractional layout widths.
- Clear filter uses the new result set immediately so browsing returns to its beginning instead of retaining a filtered result's old anchor.
- Measure card bodies and row heights locally so long titles and compatibility text do not inflate unrelated rows. Use shaped text metrics for bounded multiline titles, tighten internal vertical spacing, retain visible-card clipping and preserve browsing anchors through layout and media-state changes.
- Reuse measured geometry and known preview states across unchanged host captures so periodic refreshes do not expand failed-preview cards or remeasure unchanged titles.
- Local verification passed: 39 native installer cases across 15 locales, 540 readability checks, retained-window interactions/renders and the unchanged 250/5,000-entry performance budgets. Game visual acceptance remains user-operated.

2026-10-19 - Installer categories and initial favorite (local implementation)

- Add a folding horizontal category chooser with separate official Dalamud tags, Botology compatibility categories and third-party repository tags. Separate the two manifest-tag vocabularies by their actual feed URL, with searchable tracks and multiple required/blocked selections; matching captions in different rows remain independent. Retain selected filters when folded and keep the full vocabulary reachable without per-frame whole-list layout work.
- Reduce copying during large-catalog sorting by moving row indices while retaining prepared keys. Preserve numeric-version sorting, direction and source/Unicode identity tie order.
- Pin the Aethertek feed for fresh installations through existing configuration initialization. Preserve saved lists and permanent user removal across refreshes and updates; favorites never subscribe or install plugins.

2026-10-18 - Installer evolution (local implementation)

- Remove catalog-size hitching: index compatibility and runtime assessment matches within each capture, resolve repeated sources once, measure distinct labels and retain sorted result order. Draw visible card rows and use native clipping for table rows with measured wrapped heights. Resolve pinned authors without splitting every catalog author into temporary arrays; preserve filters, source identities, match precedence and scrolling.
- Support multiple required (green) and blocked (red) installer status, pinned repository and author filters. Left click requires, right click blocks and repeating a choice clears it. Exclude plugins unavailable on the current API by default; filter choices remain session-only.
- Correct the native title repaint for separate host font atlases and exercise the actual window hooks. Show recognizable, disambiguated feed labels, match availability to installation eligibility, use clear favourites stars and visible honest compatibility, quiet routine card actions/missing-media panels and preserve complete screenshot content with aspect-fit previews.
- Make the complete pinned-source row filter Discover and return to the results, with recognizable active-source feedback. Add a Not available on this API filter, reset result scrolling when filters change and allow clearing active filters together.
- Correct the installer presentation against the approved design: icon navigation, emphasized filter chips, an icon-preserving view switch, rounded cards with state badges and a single primary action, grouped favourites and a media/status profile. Render native candidates through the production geometry path. Resolve official artwork through its actual distribution channel and stop using Botology branding for unrelated plugins.
- Add Discover cards/table, source-aware plugin profiles, Installed, Updates, repository management and a folding favourites rail. Keep searchable unadded sources interactive, separate identical plugin names from different feeds, and retain the compatibility grid, DTR and catalog tools.
- Add explicit source changes and awaited native install/update/enable/disable/removal results while retaining installed origin, host profiles and plugin settings. Bootstrap full known feeds independently of legacy metadata-cache freshness; retain last-valid discovery data on failure.
- Save repository/author pins through the existing configuration. Hide invalid pins without deleting saved choices. Keep panel/view state session-only and preserve the logical browsing anchor when the panel folds.
- Load visible static previews and detail screenshots on demand. Play one native GIF explicitly using OS WIC decoding, frame timing, compositing and bounded media ownership; stop and release on leave/unload. Ship no media codec, helper, package or persistent media cache. WebM remains deferred.
- Translate installer controls across all 15 existing locales and preserve shared appearance preferences and native titlebar actions. Keep version 2.0.0.12; local verification is separate from live-client and Wine acceptance.

2026-10-16 - Confirmation popups without screen dimming (I576)

- Keep compatibility warnings and patch notes in titled native popups without a full-screen dimmer. Preserve dialog IDs, explicit actions and saved window opacity; outside dismissal never acknowledges a warning.

2026-10-14 - Plugin-specific Discord links (I558)

- Add an optional Discord URL to catalog entries and local editor drafts, preserving existing master/override records and defaults. Show an entry's supplied Discord link beside its repository actions and open it through the existing browser action. Require a valid Discord HTTPS URL, retain case-sensitive invite paths and disable invalid supplied links. Preserve the current body layout and unrelated fields.

2026-10-11 - Native titlebar status (I536)

- Show alert state, loaded plugin count and issue count in native main, settings and auxiliary titles. Reuse one current status snapshot per draw pass across open windows. Keep concise state and available numeric progress when detailed wording is off; hover retains full details.
- Add saved Show titlebar status and Detailed titlebar status controls in Appearance, both enabled by default. Preserve native window identities, geometry and title actions.
- Use the packaged plugin icon in settings and auxiliary titlebars. Translate the new controls and status labels in every supported language.

2026-10-11 - Appearance titlebar shortcuts (I532)

- Move Main's language, compact and transparency shortcuts into the native titlebar and add a colour palette shortcut. Other titled windows expose the same actions; retained Appearance settings and main-control visibility choices use the existing save path.
- Preserve existing title actions, window identities, geometry, complete-window opacity and release version. Popup ownership and collapsed-window actions remain subject to native and game verification.

2026-10-10 - Compact defaults and main appearance controls (I521)

- Apply Compact on once, with main Compact and Transparency controls hidden. Keep both controls and independent visibility choices in Appearance settings; preserve later choices, opacity, automation and unknown saved fields. Advance the release from 2.0.0.5 to 2.0.0.6.

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

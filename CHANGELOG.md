# Changelog

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

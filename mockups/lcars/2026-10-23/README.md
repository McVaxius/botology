# Botology LCARS concepts

Approved LCARS design concepts. Implementation is selected for the repositories in Z:\myrepos.txt, with the rollout tracked in Z:\!todo\2026-10-22GOATFUCK0002.md. These images are design references, not runtime or test evidence.

- [Main: regular and compact](01-main-regular-compact.png)
- [Settings, Mini and supporting surfaces](02-settings-mini-and-auxiliary.png)

The second sheet depicts selected existing supporting surfaces. Other source windows retain their established layout and actions under the proposed shared appearance; this is not a claim that every scroll position or conditional state has a separate image. Browser companions retain browser controls. Main/Mini windows are distinct from density variants. Generated text and examples are design material, not runtime evidence.

## Source scope

Source commit: `7f0017fb2e3b83c684e9a1643447199fd1cfb12d`. Existing dirty paths were inspected by count (0); none are included or changed by this mockup task.

## Source-verified control contract

- `botology/Windows/MainWindow.Installer.cs:119` defines Discover, Installed, Updates, Repositories, Compatibility. Repositories also owns Pinned sources; keep this complete owner navigation.
- `MainWindow.Installer.cs:369` defines Search plugins, authors or repositories, Cards/Table, filter chips, Category/Author fields and Refresh; `:567` selects Install, Manage or View details by entry state.
- `MainWindow.Installer.cs:834` opens detail with Back to results; `:970` gates plugin actions; uninstall uses Confirm uninstall / Cancel. `:1041` gates repository add/enable/remove and Confirm removal / Cancel.
- `botology/Windows/ConfigWindow.cs:44` has Settings and Window appearance tabs. `:73` starts plugin/DTR preferences; `:115` starts notification settings. `DtrManagerWindow.cs:67` defines # / DTR Entry / State / Show / Move, visibility checkbox and Up/Down.

Selected supporting surfaces are Settings, existing plugin detail, repository removal confirmation and DTR Manager. CatalogEditor and compatibility-note/warning popups retain their source layouts. No Mini exists in this inventory; Cards/Table is a presentation toggle, not density. No install, update or removal is represented as performed.

## Existing UI source inventory

- `botology/Windows/CatalogEditorWindow.cs`
- `botology/Windows/ConfigWindow.cs`
- `botology/Windows/DtrManagerWindow.cs`
- `botology/Windows/MainWindow.Categories.cs`
- `botology/Windows/MainWindow.Installer.cs`
- `botology/Windows/MainWindow.cs`

## Shared appearance contract

Auto and Picker retain their order and values; LCARS is the third option. Preserve native IDs, original actions, saved scope and semantic status colours. The proposed LCARS UI tones default off; the speaker and Settings preference are the same value, with no added volume or pack selector. Krangle obfuscates identifying text. Actual adoption waits for the FrenRider pilot.

Exact generation prompts are in [PROMPTS.md](PROMPTS.md).





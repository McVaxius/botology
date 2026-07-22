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

[Join the Discord](https://discord.gg/VsXqydsvpu)

Scroll down to "The Dumpster Fire" channel to discuss issues / suggestions for specific plugins.

## Current Status

Bootstrap scaffold created on 2026-04-07. This repo now has a buildable `Debug x64` shell with a functional compatibility grid: installed state, update availability, repo link, enable toggle, best-effort DTR toggle, ignore flags, rule detail popups, category/plugin/author filters, AI attribution, cached catalog patch notes, and a growing `plugin-repository-links.json` catalog for tracked plugins.

- Solution: `Z:\botology\botology.sln`
- Project: `Z:\botology\botology\botology.csproj`
- Commands: `/botology`, `/bottist`, `/botologist`
- Repository target: `Private`

## Plugin Concept

- Track the plugins that can overlap or fight each other.
- Default toward red when a hard conflict is present.
- Keep alerting configurable so warnings can be a toast, a popup, or a forced window open.

## Catalog notes and AI attribution

- The default-visible `AI` column marks rows whose code was identified as likely AI-written by Aetherfeed contributor and coding-pattern attribution. It is an attribution signal, not a definitive authorship finding; an unmarked row is not proven human-only.
- Attribution is frozen from Aetherfeed's `https://raw.githubusercontent.com/Aetherfeed/aetherfeed.github.io/main/public/data/plugins.json` snapshot dated 2026-07-22, SHA-256 `28e98ec13f5c2feabd9166c8a4cd3749ee42b81b6a9638175106da97ec27f7f5`. Botology does not continuously synchronize this field.
- `Patch Notes` shows the newest-first catalog release history from the last valid remote response or local cache. A failed or invalid notes fetch keeps the previous cache and does not block catalog refresh.
- The compatible `ToastOnMasterCatalogChange` setting is shown as “Toast for catalog notes affecting installed plugins.” A release is evaluated once and only produces a toast when one of its affected catalog rows matches an installed plugin; disabled plugins still count as installed.

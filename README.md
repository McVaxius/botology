# Botology

[Support development on Ko-fi](https://ko-fi.com/mcvaxius)

[Join the Discord](https://discord.gg/VsXqydsvpu)

Scroll down to "The Dumpster Fire" channel to discuss issues / suggestions for specific plugins.

## Current Status

Bootstrap scaffold created on 2026-04-07. This repo now has a buildable `Debug x64` shell with a first functional compatibility grid: installed state, update availability, repo link, enable toggle, best-effort DTR toggle, ignore flags, and rule detail popups.

- Solution: `Z:\botology\botology.sln`
- Project: `Z:\botology\botology\botology.csproj`
- Commands: `/botology`, `/bottist`, `/botologist`
- Repository target: `Private`

## Plugin Concept

- Track the plugins that can overlap or fight each other.
- Default toward red when a hard conflict is present.
- Keep alerting configurable so warnings can be a toast, a popup, or a forced window open.

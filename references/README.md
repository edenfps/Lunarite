# Reverse-engineering references

This directory contains provenance-tracked material used to reconstruct Huxley behavior.
Neither client under analysis is launched from here. The only runnable client remains
`C:\Games\Huxley\Binaries\HuxleyMMOGame.exe`.

- `quest-native-wire-audit.md` — exact US-client quest sender opcodes, formats, addresses, and
  individual/group loot reply correlation.
- `ue3-version-fingerprint.md` — Huxley's UE3 engine/package/licensee fingerprint.
- `ue3-source-symbol-leads.md` — ranked nearby UE3 source/PDB research leads.

The engine/package lineage and recommended comparison order are documented in
`ue3-version-fingerprint.md`.

## Local client references

- Korean retail client: `C:\Games\Huxley Korea` (read-only), compiled 2010-04-19.
  `binaries\HuxleyMMOGame.exe` SHA-256:
  `36DA3EAE4224A311DB10A2B7D22099BBDCC4E69A442DD08F1B08A7018632277A`.
- `korean-2010-04-19/` contains UELib text exports for comparison with US-client.

## Web archive

- `huxley-wiki/fandom-pages.json`: all 126 namespace-0 pages and revision metadata from
  `https://huxley.fandom.com/api.php`, archived through the public MediaWiki API.
- License reported by the source: CC BY-SA unless otherwise noted.
- `huxley-wiki/SHA256SUMS.txt` records archive integrity.

## External source checkouts

Ignored working checkouts live under `external/`; record the exact revisions here:

- UE Viewer (`https://github.com/gildor2/UEViewer.git`), MIT:
  `a0bfb468d42be831b126632fd8a0ae6b3614f981`.
- TERA Arise (`https://github.com/tera-arise/arise.git`), AGPL-3.0:
  `d7dce4ea3286b5415aa2ec5d25e893f3825e0237`.
- LegendaryExplorer (`https://github.com/ME3Tweaks/LegendaryExplorer.git`), GPL-3.0:
  `bd5ec550c5448e460af949c307c8d013e14f4a28`.
- Killing Floor bot client (`https://github.com/TheBestPlan/killingfloor-bot-client.git`), GPL-3.0:
  `446f90daf4ca9495db7215badc08a33c44a93516`. Its independently reversed UE2.5
  class-net-cache ordering confirmed the same base-to-derived/declaration-order algorithm used to
  reconstruct Huxley's gateway FieldNetIndex table; no code is linked into the server.

## Local engine-source reference

- A locally supplied copy of the Gears of War PC Chinese source
  tree. Its embedded authoritative fingerprint is engine `3340`, changelist `202271`, minimum and
  negotiation net version `3077`, package `490`, licensee `10`, cooked-content version `46`.
  This is the closest complete native UE3 source currently available for Huxley's custom
  `3240/496/23` build. The source remains in place and is not copied into this repository.


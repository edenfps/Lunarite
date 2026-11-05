# Client asset-reference validation

## Problem

The leaked Webzen development client does not include a complete retail `LevelPackages` set. The
working installation supplements it with Korean-client packages. Those packages make much of the
game playable, but they are compatibility evidence rather than proof of the Webzen build's original
asset layout.

UE3 normally resolves a runtime asset by its fully qualified object path, for example
`TextureIngameShop.InGameShopItem_42`. There is no single global texture-slot table. A server row,
UnrealScript default, map import, configuration value, or native constant can construct the path.
An existing package is therefore insufficient if the caller supplies an object name that is absent
from that package.

The cash shop exposed this distinction. Its numeric cooked item ID and its package-icon export name
are separate fields. Sending item ID `670101` as the icon made the client request
`TextureIngameShop.670101`, which cannot resolve. The installed package instead exports
`InGameShopItem_01` through `InGameShopItem_153` and a few named helper textures.

## Architecture

1. Inventory package exports from every client build available to the project.
2. Record package provenance, file hash, export type, and exact object name.
3. Extract dynamic references from exported UnrealScript, maps, configuration, native code, logs,
   and server-generated metadata.
4. Validate every generated reference against the manifest before it reaches a client.
5. Keep semantic IDs, wire IDs, localized labels, and asset object names as distinct typed fields.
6. Prefer an exact original mapping recovered from retail service data, binaries, or package
   references. Mark compatibility mappings explicitly when exact evidence is unavailable.
7. Create narrowly scoped compatibility packages only when the executable requires an exact object
   path absent from every supplied client package. Preserve the expected package and object names.
8. Fail tests on unresolved required references. Allow an explicit, audited fallback only for
   optional visuals.

## Reconstruction fidelity

The delivered client should look and behave as though this Webzen build shipped with its own native
content, not as though a mod pack was layered over it. Compatibility work must therefore preserve:

- the original `HuxleyMMOGame/Content` directory organization;
- Korean-client package boundaries when they are the closest surviving structural reference;
- expected package names, export names, object paths, groups, object types, and reference topology;
- the build's existing UI visual language, dimensions, materials, fonts, and localization behavior;
- UE3 cooking, compression, bulk-data, and dependency conventions compatible with this executable.

Do not introduce conspicuous `Custom`, `Fix`, `Mod`, or redirect-package names into the installed
tree when an expected retail-shaped path can be reconstructed. Do not combine unrelated assets into
one convenience package. Repository-side manifests may record provenance and compatibility status,
but those annotations must not leak into the game's UI or package namespace.

When exact Webzen evidence is unavailable, clone the closest Korean package structure and replace
only the minimum build-specific exports or references. Preserve unchanged assets byte-for-byte when
practical. Any newly cooked package must be compared against its Korean structural analogue before
installation: export/import tables, group hierarchy, class types, dependencies, flags, and naming.

## Initial implementation

`ClientAssetManifest` contains the verified `TextureIngameShop` export inventory from the installed
compatibility package. `CashShopCatalog` assigns each generated package a real export name and keeps
the cooked product ID separate. Tests require every emitted shop icon path to resolve through the
manifest.

`tools/Export-ClientAssetManifest.ps1` produces a deterministic, hashed JSON inventory from a real
package through the project's Huxley-aware UELib branch. Example:

```powershell
.\tools\Export-ClientAssetManifest.ps1 `
  'C:\Games\Huxley\HuxleyMMOGame\Content\UI\TextureIngameShop.upk' `
  -Class Texture2D -OutputPath "$env:TEMP\TextureIngameShop.manifest.json"
```

Source package:

- Path: `HuxleyMMOGame/Content/UI/TextureIngameShop.upk`
- Size: `17,527,000` bytes
- SHA-256: `6CDAEAC3BCC28CA093F84DA5D87DFE0A75E83B0ECBAE441DBA71E8B292958FA8`
- Provenance: Korean compatibility content in the US-client installation; not assumed to reproduce
  the original Webzen storefront mapping.

The initial icon assignments are compatibility mappings selected from exports that exist in that
package. Export semantics are checked visually against the cooked item class and value; merely
resolving an export is not sufficient. If original `IBSPackage.txt` data or equivalent native
evidence is recovered, replace the mapping without changing cooked product IDs.

Storefront names and summaries resolve through `ItemLocalization`: supplied Webzen INT strings are
retained, while the repository's audited localization recovery fills keys absent from the unfinished
English client. Generated rows must not expose implementation labels such as class names or
`Private-server catalog item` when a recovered client-facing string exists.

## Expansion path

- Add a deterministic UELib-based package inventory command that emits a versioned manifest.
- Extend runtime-log validation beyond `DynamicLoadObject` and missing-package diagnostics when a
  new linker failure form is observed.
- Scan UnrealScript string literals and configuration references into a required-reference set.
- Add map import tables and native string references, retaining their evidence source.
- Produce a report separating: resolved in Webzen build, resolved by Korean compatibility content,
  unresolved, type mismatch, and compatibility override.
- Build compatibility `.upk` files only for the final unresolved exact paths.

## Whole-client compatibility layer

### Package-language compatibility

The canonical development client launches with `-LANGUAGE=INT`. Approved Korean map families are
overlaid as coherent, hash-pinned units; language selection and the development UI are not replaced.
English localization aliases remain available only for Korean content that explicitly requests a
KOR filename. This separates map compatibility from global UI/package selection.

This is intentionally preferable to a binary resolver patch. The engine keeps the development
client's original INT lookup behavior while the manifest installs only evidence-backed Korean map
packages and required localization aliases. The installer retains verified backups of replaced
files and `Remove` restores them.

The UI boundary is strict. `HuxleyMMOUISkin.upk` must remain the byte-identical development package
(SHA-256 `C70C2FD606F9EF2FFD86C3E2335BC92152AC668261658EB5425EEC46DE122BFE`), and no second
`HuxleyMMOUISkin*.upk` may exist in the content directory because UE3 scans renamed `.upk` files too.
`Launch-HuxleyClient.ps1` enforces both invariants. The launcher does not install the experimental
font hybrid, a wholesale Korean skin, or scene-level UI patches. Map compatibility must leave the
development client's skin, fonts, HUD, dialogue, combat popups, and other UI packages byte-identical.
The only allowed future exception is a narrowly generated item-shop compatibility package containing
the Korean-only shop style/resource trees absent from the development skin. It must preserve every
existing development export and must pass export-level diff validation before normal installation.

The scope is every serialized asset dependency, not only server-generated shop
icons. Black world geometry and the green pebble fallback can originate from
missing packages, missing exports, class mismatches, broken material/texture
chains, or a UI skin that lacks a referenced style. A package existing on disk
does not prove that its internal dependency graph resolves in this executable.

`PackageCompatibilityAudit` inventories every `.u`, `.upk`, and `.umap` below a
client root. It records hashes, exports, classes, imports, duplicate package
names, packages that fail Huxley deserialization, missing packages, missing
exports, and class mismatches. This is the first static gate for the full
compatibility layer:

```powershell
dotnet run --project .\PackageCompatibilityAudit -- `
  C:\Games\Huxley `
  .\analysis\client-package-compatibility.json
```

The same executable validates asset paths actually requested by a running client. It reads a live
log with delete/read/write sharing, inventories the installed client once, and reports each unique
`DynamicLoadObject` path plus every `Can't find file for package` diagnostic:

```powershell
dotnet run --project .\PackageCompatibilityAudit -- --log-check `
  C:\Games\Huxley `
  C:\Games\Huxley\HuxleyMMOGame\Logs\Launch.log `
  .\analysis\client-runtime-asset-references.json
```

This is intentionally separate from the serialized graph. The graph finds dormant dependencies;
the log gate proves whether paths exercised in a test session resolve. The first post-layer scan
found 16 unique dynamic object paths, all resolved, and no missing-package diagnostic.

The resulting graph is evidence for reconstructing a retail-shaped content
tree. Fixes must operate at the dependency source (skin, material, texture,
static mesh, particle, or package reference) so every consumer is corrected at
once. The active client will not be switched wholesale to a Korean skin or a
rewritten package until the graph proves its dependencies and class shapes are
compatible.

### Proven package-resolution fault

The dev executable's async loader resolves a serialized package name to a filename before
`ULinkerLoad::CreateLinkerAsync`. In the symbol-bearing binary this is visible in the caller at
`0x109D1B90`: the map-supplied name is passed through the package-file resolver and loading fails
before a linker is created when no filename is returned. `FindPackageFile` at `0x10977580` tries
`<package>_<active-language>`, then `<package>_INT`, then the base name. Texture selection remains
data-driven by the map/UI import graph; the binary does not contain a global texture-placement
table.

The stripped Korean executable independently confirms the same resolver structure at `0x10954F10`:
active-language suffix, INT fallback, base name, then optional package-alias lookup. Its localized
filename constructor at `0x10951C80` matches the dev constructor at `0x10973A80`. The compatibility
layer therefore follows behavior present in both executables rather than inferring a new convention.

`City-SP-Nostalonia_A.war` imports `LH_CA_CityTXT_A.*`, while the installed retail content only has
`LH_CA_CityTXT_A_kor.upk`. The Korean and US copies of the `_kor` file are byte-identical
(SHA-256 `AC15FA85A7F0EDF589C0CD1DC97541005521C79B4736BC2A1EF8B4DC4276AFF5`). Live testing proved that
aliasing this package by itself is not sufficient: the dev map has 746 path/class exports absent
from the Korean map, including different baked lightmaps/model components, and additionally imports
`LH_CA_CityTXT_A.Ranockscity`, which the Korean package does not export. Mixing that dev map with
the Korean package produced white and corrupt-looking city surfaces.

The controlled visual overlay therefore treats the retail map set and its asset package as one
compatibility unit. `Install-HuxleyRetailVisualCompatibility.ps1` overlays the matching Korean
`City-SP-Nostalonia_A.war`, its streamed `City-SP-Nostalonia_ABbg.war`, the adjoining
`City-SP-Nostalonia_B.war`, and native-name `LH_CA_CityTXT_A.upk`. It hash-gates every source and
recognized dev target, saves the dev maps, verifies the installed set, and supports removal.
Dev-only script sublevels remain in place. `Launch-HuxleyClient.ps1` installs this set before the
first client starts, preventing a future launch from silently returning to mixed map/package data.

Map expansion is gated by `PackageCompatibilityAudit --map-family-report`. The report compares
every shared city `.war` by SHA-256, export path/class identity, visual and sequence export counts,
external package roots, raw sequence payloads, and decompiled sequence properties. Raw payload
differences remain diagnostic because package-local object/name indices can change without changing
behavior; classification uses resolved property text and sequence membership. It conservatively
separates visual candidates from named script/audio sublevels and excludes Simulation Center. The current evidence is stored in
`analysis/city-map-family-report.json`: 65 shared city maps comprise 43 visual candidates,
14 preserved script maps, 4 preserved audio maps, 2 maps with differing sequence-object membership,
and 2 excluded Simulation Center maps. This
prevents a filename-only bulk copy from silently replacing dev quest or transport logic.

`PackageCompatibilityAudit --map-dependency-report` also resolves every import from those 45
visual candidates against the installed client graph. It found 271 unresolved serialized imports:
205 native `Engine`/`Core` references and 66 stale `LH_CA_Natural_A` material-subobject references
used by retail Eska B and Nostalonia B. The Korean and US `LH_CA_Natural_A.upk` files are
byte-identical, proving those 66 are retail-package-internal stale references rather than a missing
compatibility copy. The bounded evidence is stored in
`analysis/city-retail-map-dependency-report.json`; package-name presence alone is not accepted as
dependency proof.

The classified rollout is encoded in `retail-visual-compatibility.json`, not an unchecked directory
copy. Its 44 hash-pinned entries contain the 43 proven visual maps plus the native-name
`LH_CA_CityTXT_A` alias. `Install-HuxleyRetailVisualCompatibility.ps1` validates that manifest,
rejects duplicate or unsafe paths and any script, audio, or Simulation Center entry, then installs,
verifies, or reversibly removes the complete set. An isolated-client round trip proves that all
retail hashes install and that every original dev map is restored byte-for-byte. This expands the
coherent Korean visual-map method across Nostalonia, Eska, and both training-center families while
preserving the development build's gameplay sublevels.

The same classifier now covers all 1,726 maps shared by the two installations. Its full report
classifies 1,668 packages through the structural/sequence-property gate, while 33 retain
dev content because logic differs, 18 named script maps and four audio maps remain dev-owned, one
non-PvP root map is held for review, and two Simulation Center maps remain excluded. PvP map assets
are included in this asset analysis and pass the same gate; this does not resume or reuse any PvP
server/protocol implementation. `retail-visual-candidates.json` persists the remaining 1,625
hash-pinned candidates after removing the 43 maps already owned by the stable city profile.
`Launch-HuxleyClient.ps1` installs both profiles. Backups for the expanded profile can live on another drive to
avoid filling the system drive. A complete verify pass checked all 1,625 installed
hashes, and Gregory Forest completed a representative live PvE test without breaking its client-side
mission behavior. Broader map-family live coverage remains incremental because those destinations
are not yet unlocked on the current test character.

The green shop fallback has a related but distinct cause. `DefaultUI.ini` requests
`HuxleyMMOUISkin.HuxleyMMOUISkin`. Under INT, the executable loads the incomplete base skin. The
installed `_kor` skin is a strict content superset for the current UI and adds the missing
`ButtonShopCash_Img`, `ButtonShopPoint_Img`, shop-tab, and item-shop style trees. It has 4,107
exports, with 3,992 export path/class pairs shared with the older clean Korean skin and 115 newer
exports, including the shop styles. This is evidence that it is the matching later retail skin,
not an arbitrary texture replacement.

`asset-compatibility.json` is the audited non-map mapping. It contains localization aliases only;
it no longer replaces the UI skin or rewrites UI configuration. `Install-HuxleyAssetCompatibility.ps1`
verifies source and existing-target hashes, stores changed originals outside the client tree,
verifies the result, and supports `Remove`.

The audit currently classifies 117 imports as exact localized aliases: one city-texture import and
116 Korean dialogue imports. UI-skin replacement is explicitly excluded.
Audio aliases remain excluded until language/audio behavior is verified.

### Green UI fallback correction

Live testing disproved the initial assumption that replacing the base UI skin would remove every
green pebble panel. Static comparison now separates three different mechanisms:

- Both builds bind empty `HuxleyUIItemSlotButton` images to
  `T_HuxleyHudGraphics.NullTex`. The dev and Korean exports are both 2x2
  `PF_A8R8G8B8` textures and their cooked mip payload is identical transparent white.
- `HuxleyMMOUI.upk` has no unresolved imports. Its only serialized `NullTex` reference in either
  build is `SelectPlayerScene.CenterDot.UITexture_27`.
- UE3 scene widgets bind skin styles through serialized GUIDs. Package names and export names can
  resolve correctly while a missing style GUID still falls back to the engine's green default
  texture. `PackageCompatibilityAudit --ui-style-audit` now validates this separate layer.

`UIInGameShop.upk` is not safe to replace wholesale with the Korean copy: the dev scene contains
9,383 exports versus 4,454 in the older retail package and includes newer inventory scenes required
by the dev scripts. Both scene/skin pairs contain the same unresolved `TradePanel2.PrimaryStyle`
GUID, so that single result is not caused by the compatibility overlay and must be checked against
the complete parent-skin chain before any package mutation. This evidence rules out a global binary
texture-index patch and a global `NullTex` replacement as valid fixes for the visible panels.

The three-widget style experiment succeeded in a controlled live test when the Korean skin supplied
all three style IDs. Against the exact development skin, only `ButtonMMOItemShop` resolves;
`ButtonShopCash_Img` and `ButtonShopPoint_Img` are absent. Therefore the experiment remains outside
the launcher: binding all three cannot satisfy the exact-dev-UI rule without first grafting only the
missing Korean item-shop style/resource trees. The PvE BP-popup investigation independently proved
that missing popups on the test character were the retail over-level reward rule (level 21 versus a level-1, 6-BP
enemy truncates the adjusted reward to zero), not this widget patch.

The current evidence instead places the fallback in runtime style/image resolution:

- The original dev skin's `ButtonMMOItemShop` states explicitly store `DefaultImage=none`.
- The installed newer skin stores `T_HuxleyHudGraphics.ItemShopIcon` and
  `ItemShopIcon_rollover` on the corresponding states.
- Both texture exports are present in the installed `T_HuxleyHudGraphics.upk`.
- The scene's absent `PrimaryStyle` is retail-shaped, so another runtime resolver path selects or
  supplies the image. That path must be identified before another package mutation is made.

`Install-HuxleyUIStyleCompatibility.ps1` remains only as a reversible research utility. It is not
part of the supported compatibility install path.

Useful focused probes:

```powershell
dotnet run --project .\PackageCompatibilityAudit -- --property-references <package> <text>
dotnet run --project .\PackageCompatibilityAudit -- --object-properties <package> <object-path>
dotnet run --project .\PackageCompatibilityAudit -- --object-layout <package> <object-path>
dotnet run --project .\PackageCompatibilityAudit -- --object-raw-info <package> <object-path>
dotnet run --project .\PackageCompatibilityAudit -- --index-objects <package> <package-index> [...]
dotnet run --project .\PackageCompatibilityAudit -- --index-references <package> <package-index>
dotnet run --project .\PackageCompatibilityAudit -- --imports <package> <path-text>
dotnet run --project .\PackageCompatibilityAudit -- --texture-info <package> <object-path>
dotnet run --project .\PackageCompatibilityAudit -- --names <package> <text>
dotnet run --project .\PackageCompatibilityAudit -- --export-data <package> <object-path>
dotnet run --project .\PackageCompatibilityAudit -- --ui-style-audit <scene.upk> <skin.upk>
dotnet run --project .\PackageCompatibilityAudit -- --inject-style <scene.upk> <widget-path> <skin.upk> <style-path> <output.upk>
```

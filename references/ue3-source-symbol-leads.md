# UE3 source and symbol leads near Huxley build 3240

Huxley's authoritative fingerprint is engine `3240`, package `496/23`. This list records public
catalog evidence only; no proprietary source or symbol archive was downloaded by this project.

## Ranked leads

### 1. Gears of War PC `v3340 Chinese + Source Code` — strongest lead

- Catalog date: 2007-11-01
- Catalog size: 3.98 GiB
- Labeled engine/source version: `3340` (only +100 from Huxley 3240)
- Gears PC's known package generation is 490; Huxley is 496.
- Expected value: closest available native UE3 networking, package-map, actor-channel, object
  serialization, UnrealScript VM, linker, and build-layout reference.
- Catalog: https://archive.thedatadungeon.com/gears_of_war_unreal_warfare_2006.html

This is the first archive to obtain. Preserve its original directory structure and any PDBs.

### 2. Gears of War `Microsoft Windows Port Source Code`

- Catalog date: 2006-10-14
- Catalog size: 19.92 GiB
- Exact embedded engine version is not stated by the catalog.
- Expected value: another early PC-native UE3 snapshot; potentially older than 3340 and therefore
  useful for bracketing changes around Huxley 3240.
- Same catalog page as above.

### 3. Gears of War prototype PDB sets

- Hidden Palace documents September 14 and September 21, 2006 Xbox 360 prototype sets containing
  root files, executables, and PDBs.
- These are Xenon symbols and predate the closest PC source, so function names and subsystem
  organization are useful while addresses, ABI, and layouts are not directly portable.
- Catalog pages:
  - https://hiddenpalace.org/Gears_of_War_(Sep_14,_2006_prototype)
  - https://hiddenpalace.org/Assets/Gears_of_War/Leftover_Sep_21,_2006_Data

### 4. Original Mass Effect 1 PC — exact engine build, no verified leaked symbols/source

- Engine version `3240`, exactly matching Huxley.
- Package version 491 versus Huxley's 496, with BioWare licensee changes.
- Searches for `MassEffect.exe.pdb`, `MassEffect.pdb`, and `PCLaunch-MassEffect.pdb` found no
  credible public PDB/source listing. A clean original PC installation remains the best exact-build
  binary/package comparison, but it is not currently a symbol lead.

### 5. Stargate Worlds beta/editor — nearby MMO, source apparently absent

- Known engine generation 3004/package 486, below Huxley but relatively close.
- A long-running preservation thread states that client source was never leaked and notes heavy
  custom networking/BigWorld integration. Its beta/editor and emulator remain useful for MMO
  architecture comparisons, but not as a native UE3 network-layout oracle.
- Reference: https://www.betaarchive.com/forum/viewtopic.php?start=525&t=25622

### 6. Lower-priority later references

- Unreal Tournament 3 September 2008 prototype reportedly contains a debug build, but UT3's engine
  generation (3809/package 512) is materially later than Huxley.
- The commonly discussed 2011 UE3 source and UDK Ultimate derivatives are much later still.
- Duke Nukem 3D Reloaded includes source/editor material but is a 2010-era UDK-derived project and
  is less useful than Gears 3340 for Huxley's core wire format.

## Files to preserve from Gears 3340

Highest priority:

- `Development/Src/Core/Inc/UnCoreNet.h`
- `Development/Src/Core/Src/UnCoreNet.cpp`
- `Development/Src/Core/Inc/UnObjVer.h`
- `Development/Src/Core/Src/UnObjVer.cpp`
- `Development/Src/Engine/Inc/UnChan.h`
- `Development/Src/Engine/Src/UnChan.cpp`
- `Development/Src/Engine/Inc/UnNet.h`
- `Development/Src/Engine/Src/UnNetDrv.cpp`
- all package-map, network GUID, bit reader/writer, linker, property serialization, and script RPC
  implementation files
- all `.pdb`, `.map`, `.lib`, `.exp`, debug executable, and generated native header files

Also preserve `Core.u`, `Engine.u`, `GameFramework.u`, `WarGame.u`, their uncooked script sources,
and any build/version logs. Do not flatten paths or discard duplicate configurations/platforms.

## Validation rule

Before treating any acquired tree as authoritative, read its embedded `ENGINE_VERSION`,
`GPackageFileVersion`, and `GEngineMinNetVersion`. Names on archive folders are secondary evidence.
Huxley's executable/packages remain authoritative whenever the reference disagrees.

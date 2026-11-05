# Huxley UE3 version fingerprint

## Conclusion

The 2010 Huxley clients use a Webzen-customized Unreal Engine 3 branch based on
**UE3 EngineVersion 3240**. Final packages use **package version 496** and
Webzen **licensee version 23** (`496/23`). This exactly identifies the
serialized engine generation, but not an Epic source-control changelist.

The closest known shipped reference is **Mass Effect 1 for Windows**, which
also reports EngineVersion 3240 (package `491`, BioWare licensee fork). Gears of
War PC is nearby at EngineVersion 3329/package 490. Unreal Tournament 3 v2.1 is
later at EngineVersion 3809/package 512.

There is no matching public UDK release. The public UDK line starts around
EngineVersion 5860/package 664, substantially newer than Huxley's base. Treating
a 2009/2010 UDK as Huxley's exact engine would introduce false package,
bytecode, networking, and native-layout assumptions.

## Direct evidence

- Both `C:\Games\Huxley` and the older read-only Korean client packages
  deserialize as EngineVersion `3240`, package `496/23`.
- UELib identifies Huxley as `496/016-023` and selects its dedicated Huxley
  branch and crypto decoder.
- UE Viewer independently detects Huxley at `402/0,10`, `491/13-16`, and
  `496/16-23`; its parser handles Huxley-specific summary and export fields.
- The US-client executable is a 32-bit MSVC 8 PE compiled 2010-06-21. Its debug
  record names the original PDB:
  `d:\huxley\Client\HuxleyStableRC\UnrealEngine3\Binaries\Lib\Release\PCLaunch-HuxleyMMOGame.pdb`.
- Embedded source paths identify Webzen's branch as
  `HuxleyStableRC\UnrealEngine3`, confirming a maintained licensee fork rather
  than a stock retail editor build.

## Reference priority

1. Huxley's executable, packages, exported UnrealScript, and Korean-client
   deltas remain authoritative.
2. Mass Effect 1 PC/LegendaryExplorer is the best public reference for base
   EngineVersion 3240 serialization and early-UE3 structures. BioWare's PCC and
   licensee changes must not be copied blindly.
3. UE Viewer and UELib contain explicit Huxley format handling and are stronger
   package-format references than a stock UDK.
4. UT3's editor and documentation help with contemporary UE3 concepts and
   tooling, but its 3809/512 engine generation is newer.
5. Earliest UDK builds are useful only as later UE3 documentation/examples.

## Sources

- UELib supported-build table: https://github.com/EliotVU/Unreal-Library
- UE Viewer Huxley detection: `external/UEViewer/Unreal/GameDatabase.cpp`
- Epic legacy UDK documentation: https://docs.unrealengine.com/udk/Main/
- LegendaryExplorer: https://github.com/ME3Tweaks/LegendaryExplorer


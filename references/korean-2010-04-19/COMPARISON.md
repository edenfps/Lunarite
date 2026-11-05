# Korean 2010-04-19 client comparison

The Korean client at `C:\Games\Huxley Korea` was inspected read-only and never launched.

- Main executable: 3,868,672 bytes, PE timestamp 2010-04-19 01:04:19 UTC-5,
  SHA-256 `36DA3EAE4224A311DB10A2B7D22099BBDCC4E69A442DD08F1B08A7018632277A`.
- It is packed/obfuscated: nonstandard writable PE sections and no readable `ProcTransmit*` or
  `ClientTcpLink.cpp` strings. Use cooked packages and final-release scripts before attempting
  binary protocol comparison.
- Assets: 1,151 `.upk` files and seven `.u` files; no `.pdb` or linker `.map` files.
- `HuxleyGame.u` SHA-256:
  `EC57732B301E19DFF0AD74226B851A4C4DB05CF31FC4011ADDB21E3EB4C747AF`.

## Data-table delta versus US-client

- Korean quest table: 662 unique `EntryIndex` values.
- US-client quest table: 680 unique `EntryIndex` values.
- Present only in Korean: `821097`, `821098`, `821099`, `822099`, `823099`, `871097`,
  `871098`, `871099`, `872099`, `873099`.
- Present only in US-client: `51`, `52`, `1044`, `1053`, `823040`-`823050`, `846106`,
  `873040`-`873050`, `896106`.
- Korean map table: 216 unique entries; US-client: 220. The four US-only entries are
  `84610500`, `84610501`, `89610500`, `89610501`.
- The Korean-only `*1097` quests are real level-10, single-player normal quests with city
  sublevels, not empty table residue. The `*3099` entries are level-30 normal quests.

UELib reports transaction-array decoding errors on this older package revision, so transaction
details in `HuxleyQuestTable.txt` are incomplete until the older struct layout is registered.

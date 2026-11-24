# Text and localization (`str` / `loc.ini`)

ELEX stores all localized text in a compiled Genome string table (`STB` version 7) inside a GAR5
resource. `data/ini/loc.ini` maps ID prefixes to the raw CSV sources and their compiled binaries used
during the build; the raw CSVs and individual `.tab` binaries are not mounted in the retail asset
catalogue.

## Purpose and where it appears

| Item | Path | Contents |
| --- | --- | --- |
| Compiled strings | `data/compiled/localization/en.str` | 41,449 ID hashes × two text columns. |
| Localization config | `data/ini/loc.ini` | 18 sections mapping prefix → raw CSV → compiled bin. |
| Export manifests | `data/compiled/*/w_*.csv` | Resource name/timestamp lists; not localized text. |

`en.str` is provided by the mounted mod volume in the reference installation; the same `STB` layout
applies to retail language files. There is no `strings.csvt` file in the mounted catalogue.

## Container

```text
GAR5                     outer resource wrapper (8 bytes before the STB header)
  uint32 0x20
STB<version>             Genome string table header (36 bytes)
  ...
```

The STB header starts at file offset `0x08`. The `STB` magic is `fourcc_le('S','T','B',version)`;
ELEX uses version **7**. All integers are little-endian.

## STB header

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint32` | `STB` + version in the high byte. |
| 4 | `uint32` | Source count. |
| 8 | `uint32` | Reserved. |
| 12 | `uint32` | Column count. |
| 16 | `uint32` | Row count (ID count). |
| 20 | `uint32` | Absolute offset of the source table. |
| 24 | `uint32` | Absolute offset of the column-name references. |
| 28 | `uint32` | Absolute offset of the column-data table. |
| 32 | `uint32` | Absolute offset of the key-table reference. |

A `streamref` is two `uint32` values: `size`, then absolute `position`.

### Sources

The source table is `sourceCount` records:

```text
uint16 pathLength
char[pathLength] csvPath
uint64 modified          // Windows FILETIME
```

`en.str` records 18 CSV sources such as `#G3:/Data/Raw/Strings/strings.csv`,
`documents.csv`, `items.csv`, `ui.csv` and `tutorial.csv`.

### Column names

The column-name table is `columnCount` `streamref` records. Each points to a byte string of the given
size (trailing NUL padding is present). `en.str` has two columns:

```text
English_Text
English_StageDir
```

### Key table

The key-table reference points to `rowCount` `uint32` ID hashes in **descending** hash order. The
hashes are the case-insensitive DJB2 values of the CSV ID strings, optionally prefixed as
`prefix:id`.

### Column data

The column-data table has `columnCount` × 16-byte records:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `streamref` | String table: `uint32[rowCount]` sequence starts + `uint16[]` sequences. |
| 8 | `streamref` | Symbol table: `uint32[]` linked symbols. |

A sequence start of `0xFFFFFFFF` means an empty string. Otherwise it indexes into the sequence array:

- a `uint16` sequence element indexes the symbol table;
- each `uint32` symbol contains a UTF-16 character in the high 16 bits and a previous-symbol link in
  the low 16 bits (`0` terminates the chain);
- the characters of one symbol chain are collected and appended **reversed**;
- sequence elements are read until a zero terminator.

This is a grammar/dictionary compression: common substrings are shared through symbol links and
reused sequences. Decoding is bounded by the row and table sizes; the parser rejects out-of-range
links and truncation.

## `loc.ini`

`data/ini/loc.ini` uses sections with three keys:

```ini
[item]
prefix=item
csv=#G3:/Data/Raw/Strings/items.csv
bin=#G3:/Data/Compiled/Strings/items.tab
```

The prefix is the namespace used when hashing IDs (`prefix + ":" + csvId`). The 18 sections are
`default`, `document`, `focus`, `focus_unique`, `info`, `infodesc`, `quest`, `questdesc`, `cutscene`,
`svm`, `skills`, `ui`, `item`, `storyprints`, `mapinfo`, `tutorial`, `achievements` and `platwin`.

The raw CSVs and per-section `.tab` files are build-time inputs and are not present in the mounted
game data; only the combined `en.str` survives.

## Lookup API

`ElexStringTable` exposes:

- columns by name (`English_Text`, `English_StageDir`);
- `TryGet(uint idHash, string? column, out string)` for direct hash lookup;
- `TryGet(string id, string? prefix, string? column, out string)`, which hashes `prefix:id` with the
  same case-insensitive DJB2 convention;
- `Find(string text, int limit)` for case-insensitive text search.

Because the compiled table stores only hashes, the original CSV ID names cannot be recovered from the
binary alone. Callers that have a template/resource ID compute the hash with the matching prefix.

## Evidence

| Metric | Value |
| --- | ---: |
| STB version | 7 |
| Sources | 18 |
| Columns | 2 (`English_Text`, `English_StageDir`) |
| Rows / ID hashes | 41,449 |
| Non-empty `English_Text` rows | 39,627 |
| Non-empty `English_StageDir` rows | 10,121 |
| Decoded text characters | 1,982,910 (`English_Text`), 407,855 (`English_StageDir`) |
| Longest decoded string | 2,254 characters |
| `en.str` size | 1,658,980 bytes |

Examples:

```text
fff85a02  English_Text      Worn Journal
fff8b11d  English_Text      This upgraded Regent sword can be further enhanced by inserting a gemstone into it.
ffefb1df  English_Text      That's it.
ffefb1df  English_StageDir  Enemy (both human and monster) has been killed. After the fight.
```

The `English_StageDir` column contains animation/context annotations such as "Parrying a sword." and
"Having watched a fight, NPC likes victor"; it is not displayed as dialogue text.

## Open questions

- The original CSV IDs are not stored in the binary; only their DJB2 hashes survive.
- Which exact prefix/name convention item and UI callers use when looking up a hash.
- Whether retail (non-mod) language files use the same STB version and column names.
- Complete `English_StageDir` semantics and how the game selects stage directions.

## Implementation status

| Component | File |
| --- | --- |
| STB parser and hash lookup API | `OpenElexSharp/Assets/ElexStringTable.cs` |
| `loc.ini` parser | `OpenElexSharp/Assets/ElexLocalizationConfig.cs` |
| CLI inspector | `OpenElexSharp/Assets/AssetCatalogConsole.cs` (`assets strings`) |
| Tests | `OpenElexSharp.Tests/ElexStringTableTests.cs`, `ElexLocalizationConfigTests.cs` |

Commands:

```powershell
dotnet run --project OpenElexSharp -- assets strings
dotnet run --project OpenElexSharp -- assets strings "Worn Journal"
dotnet run --project OpenElexSharp -- assets strings fff85a02
dotnet run --project OpenElexSharp -- assets strings --properties --limit 20
```

Output columns: table path, STB version, source/column/row counts and mount source. A hash argument
(`0x` optional) prints matching column values; other arguments search decoded text; `--properties`
prints rows, limited by `--limit` unless `--all` is used.

## References

- STB layout and decoding cross-checked with the open-source `nicodex/lianzifu` tool (MIT license),
  which supports the shared Risen 3 / ELEX string-table format.

## Related tasks

- T013 — this specification and parser.
- T202/T203 — UI, dialogue and item-name integration.

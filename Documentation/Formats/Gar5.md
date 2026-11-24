# GAR5 resources and RT02 databases

ELEX compiled assets are wrapped in a `GAR5` resource header. Many asset types also come with a `w_<type>_<generation>_<language>.db` database that maps human-readable resource names to hashed `.rom` payload files.

This document covers the wrapper, the `RT02` database layout, the name hash, and the resolved `E1RF` view. Object/property payload formats are documented separately.

## Reference implementation

The primary reference is [rmtools](https://github.com/Baltram/rmtools) (GPL-3.0), specifically the Elex Resource Manager (`mimicry/source/MimicryApp/elexresman.cpp`) and `mCGenomeVolume` (`mimicry/source/Mimicry/mi_genomevolume.cpp`). The layout below was validated against retail sound and dialogue databases.

## GAR5 wrapper

All integers are little-endian. Every compiled resource starts with:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `char[4]` | Magic `GAR5`. |
| 4 | `uint32` | Header value, observed as `0x20`. Its exact meaning is unresolved. |
| 8 | `char[4]` | Resource tag. |

The rest of the header is tag-dependent.

### `RT02` resource database header

| Offset | Type | Meaning |
| ---: | --- | --- |
| 12 | `uint32` | Class hash, e.g. `0x88E0CA06`. |
| 16 | `char[4]` | Class tag, e.g. `SN11` for sounds or `DI22` for dialogues. |
| 20 | `uint32` | Number of resource records. |

Records start at offset 24.

### Other observed tags

| Tag | Header size | Notes |
| --- | ---: | --- |
| `STB\x07` | 32 | Compiled string table. |
| `\0\0\0\0` | 32 | Generic ROM wrapper (sound/dialogue payloads, etc.). |

For non-`RT02` tags the remaining header bytes carry tag-specific metadata; the current parser skips them and reports `PayloadOffset = 32`. Header size is tag-specific, so the `0x20` value must not be treated as the total header length.

Manual validation against retail assets:

| Virtual path | Tag | Payload offset | Class |
| --- | --- | ---: | --- |
| `localization/en.str` | `STB\x07` | 32 | — |
| `0_en_snd/0/d/w_snd_0_en_d0dc3290.rom` | zeroes | 32 | — |
| `0_en_snd/w_snd_0_en.db` | `RT02` | 24 | `SN11` / `0x88E0CA06` |

## RT02 database records

Each record starts after the previous record's class block:

```
uint64[4] metadata       // timestamps and unknown values; 32 bytes
uint32 nameLength
char[nameLength] name    // Latin-1
byte separator           // observed as ',' (0x2C)
... reference/class metadata ...
uint32 classBlockSize    // counts the "GEC2" magic and the class data that follows
char[4] "GEC2"
byte[classBlockSize - 4] classData
```

The current reader locates the class block by searching for the `GEC2` marker and validating that:

- the preceding `uint32` is a plausible block size (at least 8, within the file),
- the block ends inside the database,
- the next record begins with a plausible non-empty, printable name (or, for the final record, the block ends exactly at the file end).

This avoids decoding the reference/class metadata in the middle of the record, which is not understood yet. Records are validated to consume the file exactly.

### Observed databases

| Database | Size | Records | Class tag |
| --- | ---: | ---: | --- |
| `0_en_snd/w_snd_0_en.db` | 2,322 B | 14 | `SN11` |
| `0_en_dlg/w_dlg_0_en.db` | 6,218,250 B | 31,494 | `DI22` |

Record sizes vary with the resource name and the class block. Empty resource names are not expected; malformed lengths and missing class blocks are rejected.

## Resource names, hashes and ROM files

The logical name is hashed with DJB2 after lower-casing:

```text
hash = 5381
for each Latin-1 byte c of lower(name):
    hash = (hash * 33 + c) mod 2^32
```

The hash is formatted as eight lower-case hex digits. For a database `w_snd_0_en.db` and resource `INFO_IKENI7_00001457_English`:

```text
hash      = d0dc3290
payload   = w_snd_0_en_d0dc3290.rom
directory = <db directory>/0/d/
```

The `0` is the generation from the file name; `d` is the first hash digit. Payloads live in `<db directory>/<generation>/<first hex digit>/`.

Examples verified against retail data:

| Logical name | Hash | ROM |
| --- | --- | --- |
| `INFO_PANKRATZI7_00002094_English` | `b1ccbf72` | `w_snd_0_en_b1ccbf72.rom` |
| `INFO_IKENI7_00001457_English` | `d0dc3290` | `w_snd_0_en_d0dc3290.rom` |
| `INFO_WEIDMANNX6_00007944_English` | `901f6271` | `w_snd_0_en_901f6271.rom` |

The asset layer exposes a resolved resource index with 65,965 entries from the reference installation. The logical convenience path uses the Elex Resource Manager convention, e.g. `0_en_snd/INFO_..._English.elexsnd`.

## E1RF resolved files

The Elex Resource Manager can combine a ROM payload with its database record into an `E1RF` file:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `char[4]` | Magic `E1RF`. |
| 4 | `uint32` | ROM payload size. |
| 8 | `byte[romSize]` | ROM payload, normally a `GAR5` resource. |
| 8 + romSize | `uint32` | Database class hash. |
| 12 + romSize | `uint32` | Database class revision (e.g. bytes `SN11`). |
| 16 + romSize | `uint32` | Database record size. |
| 20 + romSize | `byte[recordSize]` | Raw database record. |

Retail PAK volumes store the `GAR5` ROM and the `RT02` database separately. `E1RF` is the resolved/extracted view, not an observed stored file. The parser is provided for tooling that consumes or produces resolved resources.

## Implementation

| Component | File |
| --- | --- |
| GAR5 wrapper header | `OpenElexSharp/Assets/Gar5Header.cs` |
| RT02 database records | `OpenElexSharp/Assets/ResourceDatabase.cs` |
| DJB2 hash | `OpenElexSharp/Assets/Djb2Hash.cs` |
| Logical resource index | `OpenElexSharp/Assets/ElexResourceIndex.cs` |
| E1RF view | `OpenElexSharp/Assets/E1RfFile.cs` |

CLI:

```powershell
dotnet run --project OpenElexSharp -- assets resources --type snd --limit 20
dotnet run --project OpenElexSharp -- assets resources INFO_ --language en
```

## Open questions

- Meaning of the middle reference metadata in each database record.
- Semantics of `GAR5` header value `0x20` and the varying tag-specific header sizes.
- Exact meaning of class hash / class tag beyond identifying the resource class.
- Whether every resource type uses the same record layout (sound and dialogue are verified).
- Whether any retail or mod file stores `E1RF` directly instead of generating it.

## Related tasks

- T003 — PAK container reader.
- T004 — asset catalog and inspector.
- T004b — this format and resolver.
- T005/T006 — Genome object serialization and templates.

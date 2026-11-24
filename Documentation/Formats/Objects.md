# Genome objects (GEC2, DT01, GENOMFLE)

ELEX object data uses the Genome Engine GEL serialization family. The current implementation parses class objects (`GEC2`), DT01 document files such as `documents/*.hdr`, and the optional `GENOMFLE` string table. Property payloads are preserved byte-for-byte; type-specific decoding is added incrementally.

## Reference implementation

The primary reference is [rmtools](https://github.com/Baltram/rmtools) (GPL-3.0), especially `mCRisenDoc::DocumentRisen3Class` in `mimicry/source/Mimicry/mi_risendoc.cpp` and `mCGenomeFile` in `mi_genomefile.cpp`. Name hashes use the case-sensitive DJB2 implementation from `mCRisenName` (`g_djb2`).

## GEC2 object

All integers are little-endian.

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `char[4]` | Magic `GEC2`. |
| 4 | `uint32` | Case-sensitive DJB2 hash of the class name. |
| 8 | `uint16` | Class version. |
| 10 | `uint32` | Body size. The total object size is `14 + bodySize`. |
| 14 | `uint16` | Property count. |
| 16 | properties | See below. |
| ... | `uint32` | Class-data count. |
| ... | class data | See below. |

### Properties

Each property is:

```
uint32 typeHash              // case-sensitive DJB2 of the type name
uint32 nameHash              // case-sensitive DJB2 of the property name
uint32 dataSize
byte[dataSize] data
```

Unknown properties and unknown property types are preserved as raw bytes. Type-specific values currently known from the reference implementation include primitives, strings, GUIDs, vectors, matrices, quaternions, arrays and proxies; decoding them is a later step.

### Class data

After the property list:

```
uint32 classDataCount
repeat classDataCount:
    uint32 classHash          // case-sensitive DJB2 of the class name
    uint16 version
    uint32 dataSize
    byte[dataSize] data
```

Class-data blocks are preserved raw. Some classes (for example `gCInfo`) embed further `GEC2` objects inside their class data; those nested objects are not recursively decoded yet.

### Name hashes

Class, property-type and property-name hashes are case-sensitive DJB2:

```text
hash = 5381
for each Latin-1 byte c of name:
    hash = (hash * 33 + c) mod 2^32
```

Verified examples:

| Name | Hash |
| --- | --- |
| `class gCQuest` | `0x955C5FD7` |
| `class gCInfo` | `0x1BC7FE51` |
| `class eCDynamicEntity` | `0x36182D05` |

Resource-name hashes from `RT02` databases use the same algorithm after lower-casing; see [Gar5.md](Gar5.md).

## DT01 header documents

Retail `documents/*.hdr` files are DT01 documents:

```
char[4]  "GAR5"
uint32   header value (observed 0x20)
char[4]  "DT01"
uint32   0
uint32   1
uint32   sectionCount
uint32[sectionCount] sectionOffsets   // absolute file offsets
```

At each `sectionOffset + 8`:

```
uint32 classHash
uint32 entryCount
repeat entryCount:
    uint32 nameLength
    char[nameLength] name             // no NUL terminator
    uint64 timestamp
    uint32 objectSize                 // must equal the GEC2 object size
    GEC2 object
```

Sections and entries are validated for bounds, name lengths and object sizes. The document parser reports objects in file order.

### Observed documents

| Document | Sections | Top-level objects | Main classes |
| --- | ---: | ---: | --- |
| `documents/w_quest.hdr` | 1 | 749 | `class gCQuest` v6 |
| `documents/w_info.hdr` | 1 | 7,679 | `class gCInfo` v1 |
| `documents/w_cutscene.hdr` | 2 | 38 | two cutscene setup classes |
| `documents/w_music.hdr` | 1 | 27 | music definition class |

All 25 retail `.hdr` documents parse without errors. `w_info.hdr` contains 57,913 `GEC2` byte sequences in total; most are nested objects inside `gCInfo` class data and are intentionally preserved as raw blocks for now.

## GENOMFLE string table

Some Genome archives put strings in a shared table instead of embedding them. Layout observed from the reference implementation:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `char[8]` | Magic `GENOMFLE`. |
| 8 | `uint16` | Version (observed 1). |
| 10 | `uint32` | Offset of the footer from the start of the archive. |
| 14 | payload | Object stream. |
| footer | `uint32` | `0xDEADBEEF`. |
| +4 | `uint8` | Footer version. |
| +5 | `uint32` | String count. |
| +9 | strings | `uint16 length`, `length` Latin-1 bytes. |

Strings are referenced by 16-bit indices; embedded strings are `uint16 length` + bytes. No retail PAK asset has been observed using `GENOMFLE` yet; support is covered by synthetic fixtures.

## Implementation

| Component | File |
| --- | --- |
| GEC2 object | `OpenElexSharp/Assets/GenomeObject.cs` |
| DT01 document | `OpenElexSharp/Assets/GenomeHeaderDocument.cs` |
| GENOMFLE string table | `OpenElexSharp/Assets/GenomeStringTable.cs` |
| Case-sensitive name hash | `OpenElexSharp/Assets/Djb2Hash.cs` |

CLI inspection:

```powershell
dotnet run --project OpenElexSharp -- assets objects data/compiled/documents/w_quest.hdr --limit 20
```

Output columns: name, class hash, version, size, property count, class-data count.

## Open questions

- Property-type decoding for primitives, strings, GUIDs, vectors, arrays and proxies.
- Recursive decoding of nested `GEC2` objects inside class data.
- Class/property hash → human-readable name tables (the reference implementation ships a large name list).
- DT01 variants and `GTP0` template documents.
- Where `GENOMFLE` is used in the retail data set (none found so far).
- Version-specific class layouts.

## Related tasks

- T005 — this object model.
- T006 — templates.
- T010 — world and entity objects.

# Genome objects (GEC2, DT01, GENOMFLE)

ELEX object data uses the Genome Engine GEL serialization family. The current implementation parses class objects (`GEC2`), DT01 document files such as `documents/*.hdr`, and the optional `GENOMFLE` string table. Property payloads are preserved byte-for-byte; type-specific decoding is added incrementally.

## Reference implementation

The primary reference is [rmtools](https://github.com/Baltram/rmtools) (GPL-3.0), especially `mCRisenDoc::DocumentRisen3Class` in `mimicry/source/Mimicry/mi_risendoc.cpp` and `mCGenomeFile` in `mi_genomefile.cpp`. Name hashes use the case-sensitive DJB2 implementation from `mCRisenName` (`g_djb2`). The embedded 18,855-entry hash-to-name registry is generated from rmtools' `mi_risenname.cpp` and is attributed in `OpenElexSharp/Assets/Data/GenomeNames.json`.

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

Unknown properties and unknown property types are preserved as raw bytes. Common types are decoded:

| Type family | Decoded representation |
| --- | --- |
| `bool`, integer and floating types | Native values. |
| `class bCString` | String. |
| `class bCGuid`, `eCEntityProxy`, `eCTemplateEntityProxy` | `Guid`. |
| `bCVector`, `bCVector2`, `bCQuaternion`, `bCMatrix`, `bCBox`, `bCColor`/`bCFloatColor`, `bCRange1`, `bCEulerAngles` | Float arrays. |
| `*LocString` | Four-byte prefix is skipped, then the string. |
| String proxies (`eCScriptProxyScript`, quest/resource proxies, etc.) | String; versioned variants consume a 16-bit version first. |
| `enum ...` | Member hash, integer value and resolved member name. |
| `bT...Array<T>` / scene/ref arrays | Count plus recursively decoded elements. Elements whose layout is not known are kept as raw bytes. |
| `class bCDateTime` | UTC date/time when the FILETIME is valid. |
| `blob` | Raw bytes. |

Arrays use the reference implementation's fixed-chunk split; variable-sized element arrays fall back to raw elements when a chunk cannot be decoded.

### Nested objects

`gCInfo` version 1 class data starts with a 16-bit command count and a 16-bit condition count, followed by nested `GEC2` objects. These are decoded recursively and exposed as `GenomeObject.NestedObjects`; the retail `w_info.hdr` contains 48,493 command/condition objects. Other classes' nested data remains raw until their layouts are documented.

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

The engine embeds an 18,855-entry hash-to-name registry generated from the rmtools name list. `GenomeNameTable.TryGet` resolves classes, property types, property names and enum members; unknown hashes remain printed as `0xXXXXXXXX`. This registry is data derived from GPL-3.0 sources and is tracked with its source URL in the JSON file.

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

All 25 retail `.hdr` documents parse without errors and every property decodes or falls back to raw bytes. `w_info.hdr` contains 57,913 `GEC2` byte sequences in total; 7,679 are top-level objects and 48,493 are nested `gCInfo` commands/conditions.

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
| Property decoder | `OpenElexSharp/Assets/GenomePropertyDecoder.cs` |
| Hash-to-name registry | `OpenElexSharp/Assets/GenomeNameTable.cs`, `Assets/Data/GenomeNames.json` |
| Case-sensitive name hash | `OpenElexSharp/Assets/Djb2Hash.cs` |

CLI inspection:

```powershell
dotnet run --project OpenElexSharp -- assets objects data/compiled/documents/w_quest.hdr --limit 5 --properties
```

Output columns: name, class hash, version, size, property count, class-data count, nested count. With `--properties`, each property is printed with its resolved name, type and typed value.

## Open questions

- Recursive decoding of nested objects for classes other than `gCInfo`.
- Variable-sized array elements and scene reference arrays need broader real-data coverage.
- Class layouts for versions and classes beyond the quest/info documents.
- DT01 variants and `GTP0` template documents.
- Where `GENOMFLE` is used in the retail data set (none found so far).
- Whether the name registry covers every class/property in non-document assets.

## Related tasks

- T005 — this object model.
- T006 — templates.
- T010 — world and entity objects.

# PAK archives (G3V0)

ELEX stores its compiled assets in `data/packed/*.pak` volumes. This document specifies the container, its directory tree and compression. It does **not** describe the GAR5 resource wrapper inside each file; that belongs to the asset formats.

## Reference implementations

This specification was derived from asset analysis and the following open-source projects. No code was copied; the implementations below were used as format references and cross-checks.

| Project | License | Contribution |
| --- | --- | --- |
| [rmtools](https://github.com/Baltram/rmtools) — Elex Resource Manager (`mimicry/source/Mimicry/mi_genomevolume.cpp`) | GPL-3.0 | Primary reference for the ELEX G3V0 header, Risen-3-style directory entries, attributes, sizes and DEFLATE compression. |
| [GameExtractor](https://github.com/wattostudios/GameExtractor) — `Plugin_PAK_G3V0_2` | GPL-2.0-or-later | Independently documents the Risen-family directory layout that ELEX reuses. |
| [G3Archive](https://github.com/gekonnn/G3Archive) | MIT | Gothic 3 ancestor format; confirmed the G3V0 header lineage. |

## Header

All integers are little-endian. The fixed header is 48 bytes.

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint32` | Version. ELEX uses `1`; version `0` is the older Gothic 3 layout and is not supported. |
| 4 | `char[4]` | Magic `G3V0`. |
| 8 | `uint32` | Revision (observed `0`). |
| 12 | `uint32` | Encryption mode. Must be `0`; encrypted volumes are unsupported. |
| 16 | `uint32` | Compression mode (observed `1`; values up to `2` are accepted). |
| 20 | `uint32` | Reserved marker, `0xFEEDFACE` in ELEX volumes. |
| 24 | `uint64` | File-data offset; always `48` in observed volumes. |
| 32 | `uint64` | Directory offset. |
| 40 | `uint64` | Volume size. In retail volumes this equals the physical file length. |

File data occupies `[fileDataOffset, directoryOffset)`. The directory tree ends exactly at `volumeSize`.

## Strings

A file/directory name is stored as:

```
uint32 length          // bytes, excluding terminator
byte[length] name      // Latin-1
byte 0                 // terminator, present when length > 0
```

The root directory's name is empty. Path components use `/` or `\`; the reader normalizes both to `/`.

## Directory tree

The root starts at `directoryOffset`:

```
FileString name                // empty for the root
uint64 createdTime             // Windows FILETIME
uint64 accessedTime
uint64 modifiedTime
uint32 attributes              // Packed | Directory = 0x00020010
uint32 entryCount
entry[entryCount]
```

Each entry starts with its attributes:

```
uint32 attributes
```

If `attributes & 0x10` is set it is a directory:

```
FileString name
uint64 createdTime
uint64 accessedTime
uint64 modifiedTime
uint32 attributes              // Packed | Directory
uint32 entryCount
entry[entryCount]
```

Otherwise it is a file:

```
FileString name
uint64 dataOffset              // absolute offset into the volume
uint64 createdTime
uint64 accessedTime
uint64 modifiedTime
uint32 attributes              // Packed | Archive, optionally Compressed
uint32 unknown                 // observed 0
uint32 compressed              // 0 = stored, non-zero = raw DEFLATE
uint32 storedSize              // bytes in the volume
uint32 size                    // bytes after decompression
```

The directory's `entryCount` counts files and subdirectories. The reference writer emits files before subdirectories, but readers must not depend on that order.

### Attributes

| Bit | Name | Meaning |
| ---: | --- | --- |
| `0x00000010` | Directory | Entry is a directory. |
| `0x00000020` | Archive | Entry is a regular file. |
| `0x00000800` | Compressed | Informational; the `compressed` field is authoritative. |
| `0x00004000` | Encrypted | Entry is not usable; skip it. |
| `0x00008000` | Deleted | Entry is not live; skip it. |
| `0x00010000` | Virtual | Virtual/overlay entry. |
| `0x00020000` | Packed | Entry comes from a volume. |

Files whose prefix or second attribute field contains `0x0000C000` are skipped.

## Compression

When the file entry's `compressed` field is non-zero, `storedSize` bytes at `dataOffset` are a **raw DEFLATE stream (RFC 1951)** — there is no zlib or gzip header. Inflate them and verify that the result is exactly `size` bytes.

This was verified on the retail archive: `staticscene/World/World.ssdb` is stored as 1,883,309 bytes and inflates to 7,185,628 bytes beginning with `GAR5`. The `zlib` wrapper is absent; using a raw-deflate decoder works.

Uncompressed entries have `storedSize == size`.

## File contents

Every compiled resource in the retail volumes usually begins with a `GAR5` resource wrapper, but some files (for example `.csv` tables) are plain data. That wrapper, the `RT02`/`SNxx` sections and `<db>` resource databases are asset-level structures. The PAK reader returns the stored bytes unchanged (after archive-level DEFLATE decompression, if any).

ELEX resource databases (`w_<type>_<generation>_<language>.db`) map hashed resource names to `.rom` payloads and use an `E1RF` wrapper. Resolving those into named resources is a later asset-layer task, not part of the container.

## Observed retail volumes

| Volume | Size | Entries | Compressed | Directory end |
| --- | ---: | ---: | ---: | --- |
| `c_0_en.pak` | 84 B | 0 | 0 | == volume size |
| `c_0_na.pak` | 84 B | 0 | 0 | == volume size |
| `c_1_en.pak` | 3,096,676,461 B | 31,511 | 0 | == volume size |
| `c_1_na.pak` | 24,150,135,767 B | 124,516 | 110,782 | == volume size |
| `m_1_elex_overhaul_plus_v1_3.pak` | 5,714,032 B | 880 | 879 | == volume size (mod overlay) |

The two `c_0` volumes are empty stubs. `c_1_na` contains world/static-scene data; `c_1_en` contains localization, sound and dialogue resources.

## Implementation

| Component | File |
| --- | --- |
| Volume parser | `OpenElexSharp/Assets/PakArchive.cs` |
| Entry model | `OpenElexSharp/Assets/PakEntry.cs` |
| Bounded data stream | `OpenElexSharp/Assets/BoundedReadStream.cs` |
| PAK-backed asset source | `OpenElexSharp/Assets/PackedAssetSource.cs` |

`PackedAssetSource` loads every `*.pak` in a mount folder in file-name order. Later volumes shadow earlier ones, so `m_*` mods override `c_*` base volumes. Broken or empty mod volumes are skipped with a diagnostic instead of failing startup. Compressed entries declaring more than 512 MiB of output are rejected as a safety limit; the largest observed asset is about 172 MiB.

## Open questions

- Meaning of the header `compression` mode; decompression currently follows each entry's flag.
- Whether the reserved `0xFEEDFACE` marker is required or merely informational.
- Exact semantics of deleted entries and root names in third-party volumes.
- Language selection: all volumes (`_en`, `_na`, ...) are currently mounted; a language preference is not implemented.
- Whether non-empty root names prefix entries in all third-party volumes (the root name is used as a path prefix).

## Related tasks

- T002 — installation and mount system.
- T003 — this reader.
- T004 — asset catalog over the archive index.
- T005/T006 — GAR5 resource databases and templates.

# Asset catalog and inspector

`AssetCatalog` is a searchable metadata snapshot of everything exposed by the virtual file system. The headless `assets` commands use it to inspect packed and loose files without launching the game window or writing throwaway C# code.

## Commands

```powershell
dotnet run --project OpenElexSharp -- assets help
dotnet run --project OpenElexSharp -- assets stats
dotnet run --project OpenElexSharp -- assets list [substring] [options]
dotnet run --project OpenElexSharp -- assets find <substring> [options]
dotnet run --project OpenElexSharp -- assets resources [substring] [options]
dotnet run --project OpenElexSharp -- assets objects <assetPath> [--limit <n>|--all] [--properties] [--elex-path <path>]
dotnet run --project OpenElexSharp -- assets templates [substring] [--limit <n>|--all] [--properties] [--elex-path <path>]
dotnet run --project OpenElexSharp -- assets textures [substring] [--limit <n>|--all] [--properties] [--decode] [--elex-path <path>]
dotnet run --project OpenElexSharp -- assets meshes [substring] [--limit <n>|--all] [--properties] [--elex-path <path>]
dotnet run --project OpenElexSharp -- assets materials [substring] [--limit <n>|--all] [--properties] [--elex-path <path>]
dotnet run --project OpenElexSharp -- assets skeletons [substring] [--limit <n>|--all] [--properties] [--elex-path <path>]
dotnet run --project OpenElexSharp -- assets animations [substring] [--limit <n>|--all] [--properties] [--elex-path <path>]
dotnet run --project OpenElexSharp -- assets blendshapes [substring] [--limit <n>|--all] [--properties] [--elex-path <path>]
dotnet run --project OpenElexSharp -- assets export <assetPath> <destination> [--elex-path <path>]
```

All commands accept `--elex-path <path>`; otherwise `ELEX_INSTALL_PATH` is used.

### `list` / `find`

Prints one matching asset per line as tab-separated `path`, logical size in bytes, and source:

```
data/compiled/localization/en.str	1658980	G3:data/compiled/m_1_elex_overhaul_plus_v1_3.pak
```

| Option | Meaning |
| --- | --- |
| positional / `--filter <text>` | Case-insensitive substring of the virtual path. |
| `--ext <ext>` | Extension filter; `dds` and `.dds` are equivalent. |
| `--source <text>` | Case-insensitive substring of the source/mount name, for example `c_1_na`. |
| `--language <code>` | Language inferred from retail volume names (`c_1_en` → `en`). |
| `--limit <n>` | Maximum lines; default `100`. |
| `--all` | No limit. |
| `--include-binaries` | Include `.exe`, `.dll` and similar files in the listing. |

Executables are hidden from `list`/`find` by default. Machine-readable output goes to stdout; the match summary goes to stderr.

### `stats`

Prints total asset count, packed/loose split, executable count, total logical size, and tables grouped by extension and by source. Sizes are logical (decompressed) sizes, not bytes stored in the PAK.

### `resources`

Builds the logical resource index from `RT02` databases and prints resolved resources as tab-separated `name`, `size`, `type`, `language`, logical `.elex<type>` path, and ROM path. Supports `--type <token>` (for example `snd` or `dlg`), `--language`, a name substring, and the usual limit options.

```powershell
dotnet run --project OpenElexSharp -- assets resources --type snd --limit 20
```

### `objects`

Parses a `DT01` header document (for example `data/compiled/documents/w_quest.hdr`) and prints its top-level `GEC2` objects as tab-separated `name`, class hash, version, size, property count and class-data count. A single `GEC2` object and `GENOMFLE` string tables are also recognized. Use `--limit`/`--all` to control output.

```powershell
dotnet run --project OpenElexSharp -- assets objects data/compiled/documents/w_quest.hdr --limit 20
dotnet run --project OpenElexSharp -- assets objects data/compiled/documents/w_quest.hdr --limit 5 --properties
```

### `templates`

Builds the template database from `tpl` resources and prints name, GUID, resolved parent, inheritance depth, own/inherited/full-composition property-set counts and child count. `--properties` prints every property set in the full composition with typed values.

```powershell
dotnet run --project OpenElexSharp -- assets templates 1H_Axe --limit 5 --properties
```

### `textures`

Builds the logical resource index, finds `img` resources and parses their DDS headers. Prints name,
width, height, mip count, format, sRGB flag, volume/cubemap flags, array size, logical size and ROM
path. `--properties` prints header offsets, FourCC/DXGI, caps, masks and every mip level.
`--decode` decodes mip 0 to RGBA8 and prints an FNV-1a checksum; volume textures are reported as
decode failures because they cannot be uploaded as a `Texture2D`.

```powershell
dotnet run --project OpenElexSharp -- assets textures AS_DirtMasks_X3 --properties --decode
dotnet run --project OpenElexSharp -- assets textures --all
```

### `meshes`

Builds the logical resource index, finds `msh` resources and parses their GMSH v2 geometry: the GPU
index buffer, vertex streams, attribute formats and buffer descriptors. Prints name, vertex count,
triangle count, attribute/stream/descriptor counts, bounds and ROM path. `--properties` prints the
decoded stream layout, formats, buffer descriptors, the maximum deviation between packed and widened
positions, and the `eCMeshResource2` submesh/material/LOD metadata from the database.

```powershell
dotnet run --project OpenElexSharp -- assets meshes Obj_Bld_Alb_Door_Closed_L1 --properties
dotnet run --project OpenElexSharp -- assets meshes --all
```

### `materials`

Parses `mat` resources, their texture entries and the `InstanceMaterial` shader name from the
material database. Prints name, texture count, template name, shader, primary texture and ROM path.
`--properties` lists every texture with its inferred role.

```powershell
dotnet run --project OpenElexSharp -- assets materials AS_AlbSigns_1 --properties
dotnet run --project OpenElexSharp -- assets materials --all
```

### `skeletons`

Parses `skn` resources (skinned GMSH meshes with an embedded skeleton). Prints name, vertex/index
counts, bone count, name count, bone names and ROM path. `--properties` lists the skeleton header,
bind-pose bones, DJB2 name hashes, name offsets and parent indices. The command reports the maximum
bone count across all inspected resources.

```powershell
dotnet run --project OpenElexSharp -- assets skeletons CH_Head_F_Ambient_01_L1 --properties
dotnet run --project OpenElexSharp -- assets skeletons --all
```

### `animations`

Parses `net` resources (Morpheme motion-network definitions). Prints name, readable-name count, `BONE_`-prefixed bone-name count, clip count, posed-clip count, simple-transition count, state-name count and ROM path. `--properties` lists every readable name, each clip's tracks/poses, then every transition, state and graph node. Readable names are capped at 100,000 per resource; the large `Male` and `Creature` networks reach the cap.

The parser decodes static clip poses, maps track names to `skn` skeleton bones (exact names, or `BONE_`-prefixed tracks), and derives state-machine transitions from `<Source>_<Target>|ConditionGroup|<Condition>` names. Binary graph edges, hierarchical `Male` paths and keyframe curves are not decoded yet; see [Formats/Animations.md](Formats/Animations.md).

```powershell
dotnet run --project OpenElexSharp -- assets animations Chest --properties
dotnet run --project OpenElexSharp -- assets animations --all
```

The network graph, state transitions and keyframe curves are not decoded yet; each clip currently exposes a single static pose.

### `blendshapes`

Parses `bln` resources (blend-shape target sets). Prints name, target count, base-mesh vertex count, name count, header fields, decoded delta count, whether deltas were decoded, the first target names and ROM path. `--properties` lists every target with its relative name offset, delta count and the first two position/normal deltas.

```powershell
dotnet run --project OpenElexSharp -- assets blendshapes CH_Head_F_Ambient_01_L1 --properties
dotnet run --project OpenElexSharp -- assets blendshapes --all
```

The vertex remap, sparse per-vertex target masks and quantized position/normal deltas are decoded; `ElexBlendShapeSet.ApplyWeights` applies weights to a base-mesh buffer. See [Formats/Animations.md](Formats/Animations.md).

### `export`

Copies one asset through the virtual file system, creating parent directories. The destination is overwritten. Executable formats are refused and missing assets return a non-zero exit code.

```powershell
dotnet run --project OpenElexSharp -- assets export data/compiled/localization/en.str out/en.str
```

## Catalog model

| Type | Purpose |
| --- | --- |
| `AssetCatalog` | Sorted entries, path lookup, statistics. |
| `AssetCatalogEntry` | Path, logical size, source, extension, language, packed flag, executable flag. |
| `AssetStatistics` / `AssetGroupStatistics` | Totals and grouped counts/sizes. |
| `AssetKinds` | Executable-extension classification. |
| `ElexResourceIndex` / `ElexResource` | Logical name → hashed ROM resolution through `RT02` databases. |
| `GenomeObject` / `GenomeHeaderDocument` / `GenomeStringTable` | GEC2 objects, DT01 documents and GENOMFLE string tables. |
| `GenomeTemplate` / `TemplateDatabase` | GTP0 templates, child composition and inheritance resolution. |
| `DdsImage` / `DdsTextureDecoder` | DDS headers, mip/face layout and CPU decoding. |
| `TextureLoader` | Compressed or decoded upload to `Texture2D`, plus fallback textures. |
| `GmshMesh` | GMSH v2 geometry, GPU indices, packed vertex streams and buffer descriptors. |
| `MeshMetadata` / `ResourceClassBlockReader` | `eCMeshResource2` submesh ranges, material names and LOD distances from `w_msh` databases. |
| `ElexMaterial` / `MaterialMetadata` | Material payload texture entries and `InstanceMaterial` shader names. |
| `MeshLoader` / `DrMeshPartTag` | DigitalRiseModel `DrMesh`/`DrMeshPart` creation with real normals, UVs, submesh ranges and material references. |
| `ElexMaterial` / `MaterialLibrary` | Material payload parsing, runtime shader/texture attachment and a shared upload cache. |
| `Skeleton` | Embedded `skn` skeleton tail: bind-pose bones, parent indices, name offsets, DJB2 hashes and names. |
| `MotionNetwork` | `net` Morpheme motion-network readable names, clip tables and static clip poses. |
| `MotionClip` | One clip: track names and per-track quaternion pose. |
| `MotionTransition` | Parsed `Source_Target|ConditionGroup|Condition` graph transition. |
| `MotionPose` / `SkeletonPose` | Retargeting, clip blending and skeleton world-transform composition. |
| `ElexBlendShapeSet` | `bln` header, target names, vertex remap, sparse descriptors, quantized deltas and weight application. |
| `ElexBlendShapeTarget` / `ElexBlendShapeDelta` | Named target with sparse position/normal deltas. |
| `Renderer` | Low-level device state, stock effects, draw calls and mesh/texture statistics. |
| `Hud` | Myra overlay showing FPS, draw calls, triangles, mesh/part count and the Escape control hint. |

The catalog is metadata-only: building it does not open payloads. Inspectors read payloads on demand, and `export` is the only command that writes asset bytes.

## Performance and caching

On the reference installation the catalog contains 156,954 entries (156,905 packed, 49 loose). A cold `assets stats` run, including mounting and parsing all PAK indexes, takes about 2.4 seconds. The catalog itself builds in well under a second after mounting.

`assets resources` additionally parses every `RT02` database; a cold run takes about 3.5 seconds and resolves 65,965 logical resources.

`assets textures --all` parses all 9,631 image headers in roughly 20-30 seconds including resource-database discovery. Decoding every mip-0 texture with `--decode` takes several minutes and is intended as a diagnostic rather than an interactive command.

`assets meshes --all` parses all 5,868 static meshes and their database metadata; `--properties` takes about 35 seconds including resource-database discovery and 13,406 submesh records.

`assets materials --all --properties` parses all 1,726 materials in a few seconds.

`assets animations --all` parses all 34 motion networks in roughly 5 seconds. The 59.6 MB `Male` network dominates the readable-name and transition scans (names capped at 100,000). `assets blendshapes --all` fully decodes all 270 blend-shape sets in roughly 35 seconds (63.5 million deltas); a filtered or single-resource query is near-instant.

PAK indexes are parsed on every process start. A persistent index cache is deliberately not implemented yet: the current cost is acceptable and avoids cache invalidation bugs. It is tracked in the backlog.

## Language filter

Language is inferred only from retail-style volume names matching `c_<generation>_<code>.pak` or `m_<generation>_<code>.pak`, using the known game language codes. Loose files and differently named mods have no inferred language; use `--source` for those.

## Limitations

- All language volumes are mounted; `--language` filters the catalog rather than selecting a single volume.
- Path search is substring-based, not glob-based.
- Statistics use logical sizes, not archive storage size.
- Executable classification is extension-based.
- Resource database records are located via their `GEC2` class blocks; the reference metadata in the middle of a record is not decoded yet.
- Object properties and class data are preserved raw; type-specific decoding and nested `GEC2` objects are not implemented yet.
- `assets textures --all --decode` is intentionally a diagnostic path: decoding every mip-0 texture in the retail set takes several minutes. Normal browsing should use a filter or omit `--decode`.
- Volume textures are parsed but not uploaded; cubemap/array upload is not implemented.
- GMSH meshes expose the GPU index buffer, packed positions/normals/tangents/colors/UVs, buffer descriptors and database-side submesh/material/LOD metadata. Vertex colors and tangents are decoded but not rendered by the stock `BasicEffect` path; the material parameter blocks are not decoded.
- Material texture binding is explicit through the mesh database's submesh material names. A mesh whose material has no texture entry renders with the checker fallback.
- `skn` skeletons parse for 1,270 of 1,320 resources. The remaining 50 creature/female/hologram tails use an unrecognized layout; body bind-pose transforms are not decoded yet.
- `net` motion networks expose readable names, one static pose per clip, and name-derived simple transitions/state names; binary graph edges, hierarchical `Male` paths, transition timing, keyframe curves and compression are not decoded.
- `bln` blend shapes decode the vertex remap, sparse target masks and quantized position/normal deltas; the exact game unit, base-mesh vertex-order contract and normal normalization convention remain to be confirmed.

## Related tasks

- T002 — mounts.
- T003 — PAK reader.
- T004 — catalog and inspector.
- T004b — GAR5 wrappers, resource databases and logical resource names.
- T005 — Genome object model.

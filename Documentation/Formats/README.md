# Format specifications

This folder documents the binary formats used by ELEX assets. Each specification must be detailed enough for an independent implementation and must be based on asset analysis only.

## Required sections

1. **Purpose** — what the format stores and where it appears (relative paths, extensions).
2. **Container / magic / version** — signature bytes, version fields, endianness, alignment.
3. **Layout** — sequential tables of offsets, types, sizes and meanings. Use explicit integer widths (e.g. `uint32`, `int16`).
4. **Encoding** — compression, varints, string encoding, coordinate conventions (handedness, units), quaternions, texture swizzles.
5. **References** — how the format points at other assets (by GUID, path, hash, index) and how those resolve through mounts.
6. **Evidence** — small hex excerpts and relative asset paths that justify the layout.
7. **Open questions** — unresolved fields and observed-but-unexplained data. Never silently guess.
8. **Implementation status** — pointer to the loader class and tests, which parts are supported, which are not.
9. **References** — links to open-source projects or public documentation used for research (with license), if any.

## Rules

- Never derive formats from game executables, DLLs or runtime memory.
- Prefer describing multiple observed variants of a structure over assuming one fixed layout.
- Keep examples tiny (≤ 64 bytes) and free of bulk copyrighted content.
- When the implementation contradicts the spec, fix the spec in the same change.
- Use `unknown_XX` naming for unidentified fields until they are understood.

## Implemented specs

- [Pak.md](Pak.md) — G3V0 PAK archives (T003).
- [Gar5.md](Gar5.md) — GAR5 resource wrappers, RT02 databases and E1RF files (T004b).
- [Objects.md](Objects.md) — GEC2 objects, DT01 documents and GENOMFLE string tables (T005/T005b).
- [Templates.md](Templates.md) — GTP0 entity/item templates (T006).
- [Textures.md](Textures.md) — DDS image resources, BC1–BC7 and texture upload (T007).
- [Meshes.md](Meshes.md) — GMSH v2 static meshes, vertex streams and submesh metadata (T008/T008b).
- [Materials.md](Materials.md) — GAR5 material payloads, texture entries and shader metadata (T008b).
- [Skeletons.md](Skeletons.md) — `skn` skinned meshes and embedded skeletons (T009a, partial).
- [Animations.md](Animations.md) — Morpheme motion networks (`net`), clips/poses, transitions and blend shapes (`bln`) (T009b/T009c/T009d/T009e, partial).
- [Worlds.md](Worlds.md) — world definitions (`wrl`), scene sectors (`sec`), entity trees and transforms (T010).
- [Collision.md](Collision.md) — collision (`col`), occlusion (`ocm`) and navigation annotations (T011).
- [Audio.md](Audio.md) — FSB5 sound banks (`snd`/`dlg`) and FADPCM metadata (T012).

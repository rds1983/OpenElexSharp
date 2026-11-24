# Documentation

Design notes and reverse-engineered file-format specifications for OpenElexSharp.

Format knowledge is derived **only** from game assets — never from `ELEX.exe` or shipped libraries. Every spec is written before (or alongside) its loader and is kept up to date when the implementation discovers new details.

## Index

| Document | Status | Related task |
| --- | --- | --- |
| [Assets.md](Assets.md) | implemented | T004 |
| [Mounts.md](Mounts.md) | implemented | T002 |
| [UI.md](UI.md) | implemented | T008/T101 |
| [Formats/README.md](Formats/README.md) | living | — |
| [Formats/Pak.md](Formats/Pak.md) | implemented | T003 |
| [Formats/Gar5.md](Formats/Gar5.md) | implemented | T004b |
| [Formats/Objects.md](Formats/Objects.md) | implemented | T005 |
| [Formats/Templates.md](Formats/Templates.md) | implemented | T006 |
| [Formats/Textures.md](Formats/Textures.md) | implemented | T007 |
| [Formats/Meshes.md](Formats/Meshes.md) | draft | T008 |
| Formats/Skeletons.md | planned | T009 |
| Formats/Worlds.md | planned | T010 |
| Formats/Collision.md | planned | T011 |
| Formats/Audio.md | planned | T012 |
| Formats/Localization.md | planned | T013 |
| Formats/Scripts.md | planned | T014 |
| Architecture.md | planned | T101 |

Status values: **planned** (not started), **draft** (written, implementation pending or partial), **implemented** (loader exists and tests cover it), **deprecated**.

## Conventions

- One topic per file, Markdown, relative links.
- Cite evidence from asset data with small hex excerpts and relative asset paths; never include large data dumps or absolute local paths.
- Record open questions instead of guessing. Guesses that later become facts must be marked as verified.
- Add research credits to the README when another open-source project informs a spec.

# OpenElexSharp

An open-source, from-scratch C# implementation of the **ELEX** engine and game runtime (Piranha Bytes, 2017), built on MonoGame.

> **Status: pre-alpha.** Asset mounting, format parsing, texture loading and static-mesh preview. There is no playable build yet; the roadmap lives in [TASKS.md](TASKS.md).

OpenElexSharp reads assets from a local ELEX installation. **No game assets or game code are distributed with this project.**

## Technology

| Area | Technology |
| --- | --- |
| Language / runtime | C# 14 on .NET 10 |
| Framework | [MonoGame](https://monogame.net) (DesktopGL) |
| Models & animation | [DigitalRiseModel](https://github.com/rds1983/DigitalRiseModel) |
| Textures | MonoGame surface formats + [BCnEncoder.NET](https://github.com/Nominom/BCnEncoder.NET) for BC6H/BC7 CPU decoding |
| UI | [Myra](https://github.com/rds1983/Myra) |
| Physics | [BepuPhysics2](https://github.com/bepu/bepuphysics2) |
| Tests | xUnit |

## Priorities

1. **Gameplay first.** Make every gameplay-relevant entity (hero, NPCs, monsters, objects, levels) visible and usable.
2. **Then systems.** Activities, scripts, combat, story, audio — until the game can be finished.
3. **Then parity.** Visual and UI parity are explicitly the last milestone.

Rendering starts with stock MonoGame effects (`BasicEffect`, `SkinnedEffect`, …); a custom effect is added only where strictly required (for example, models exceeding `SkinnedEffect`'s bone limit). UI starts with plain Myra widgets and is only polished during the parity phase.

## Layout

```
Documentation/        design notes and reverse-engineered format specs (Markdown)
OpenElexSharp/        engine + game source and project file
OpenElexSharp.Tests/  xUnit tests
OpenElexSharp.slnx    solution
README.md             this file
TASKS.md              roadmap and current task
AGENTS.md             workflow for AI agents and contributors
```

Machine-specific settings (such as the local ELEX installation path) live in `AGENTS.local.md`, which is git-ignored.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A legitimate ELEX installation (Steam/GOG). Assets are loaded from it at runtime.

## Development

```powershell
$env:ELEX_INSTALL_PATH = 'C:\Games\ELEX'   # or pass --elex-path <path>
dotnet build OpenElexSharp.slnx
dotnet test OpenElexSharp.slnx
dotnet run --project OpenElexSharp
dotnet run --project OpenElexSharp -- --mesh Mesh_Male_FullBody_Alb_High_01_L1 --animation Male
```

`--smoke-test` renders a few frames and exits with code 0 (used for automated verification). At this stage the engine mounts the installation's loose folders and PAK volumes from `mount_packed.xml` and opens a window. `--texture <image resource>` displays a game DDS texture and `--mesh <mesh resource>` displays a static GMSH mesh with its decoded normals, UVs, submesh ranges and material textures. `--viewer` opens the mesh viewer at the first catalog mesh; `Left`/`Right` cycles all 5,868 meshes, `Home`/`End` jumps to the ends, `V` toggles unlit vertex colors, `T` draws tangent lines and `C` cycles face culling. `--stress` opens an 864-instance static-batched test grid with a free-fly camera (`WASD`/`QE`, `Shift`, right-mouse or arrow look) and `F2` toggles frustum culling; `--no-cull` starts with culling off. `--sector <sec resource>` loads a static scene sector with its entities, shared meshes, fog and framed camera; repeat it to combine sectors (for example `ABE_AbessaCity_Buildings` and `ABE_AbessaCity_Decoration`). Region palettes provide a sky gradient, sun/fill lights, ambient/emissive and matching fog for Tavar, Edan, Ignadon and Xacor. Add `--terrain` to load the 137 × 137 heightmap chunks embedded in the static-scene blobs around the sector bounds; they render through the same scene/renderer path with one shared grass material, shared-edge stitching and outer-boundary skirts until terrain splatting is decoded. Add `--visuals` to load nearby light-source, decal, water/lava and nature/SpeedTree companion sectors filtered to the base scene; while a sector is loaded, `1`–`4` toggle lights, decals, water and vegetation and `0` restores all categories. `--character <skn resource>` opens the character viewer and renders a humanoid/creature bind pose through an `ElexModel` (a `GothicModel`-style DigitalRiseModel wrapper with `DrModel`/`DrModelInstance`/`DrSkin`) and the stock `SkinnedEffect`; add `--animation <net resource>` to create the DigitalRiseModel `AnimationController` (retail keyframe tracks are still compressed, so the bind pose remains until a clip is started through `CharacterViewer.Animation`). The overlay reports bone, part and material counts. `--mesh <skn resource> --animation <net resource>` displays the skeleton bind pose and reports the motion-network clip/sample-candidate counts; the 16-byte sample candidates are diagnostic only. `--collision <col resource>` draws a collision wireframe (scaled from metres to mesh centimetres) and can be combined with `--mesh`; add `--smoke-test`/`--capture <path>` for automated checks, and `--capture-frame <n>` to save a later frame. All scenes show a Myra HUD with FPS, update/draw time, memory, draw-call, triangle, drawn/culled and batch statistics; press `F1` to toggle the stats block and `Escape` to exit. `--soak-test <seconds>` runs the loop for a fixed duration and logs memory samples; `--crash-test` deliberately fails to exercise the crash screen. Runtime logs go to `logs/openelexsharp.log`, crash reports to `logs/crash-*.txt`. Full materials, skinned meshes and world rendering arrive in later tasks.

Asset inspection is available headlessly:

```powershell
dotnet run --project OpenElexSharp -- assets stats
dotnet run --project OpenElexSharp -- assets list dds --limit 20
dotnet run --project OpenElexSharp -- assets resources --type snd --limit 20
dotnet run --project OpenElexSharp -- assets objects data/compiled/documents/w_quest.hdr --limit 20
dotnet run --project OpenElexSharp -- assets objects data/compiled/documents/w_quest.hdr --limit 5 --properties
dotnet run --project OpenElexSharp -- assets templates 1H_Axe --limit 5 --properties
dotnet run --project OpenElexSharp -- assets textures AS_DirtMasks_X3 --properties --decode
dotnet run --project OpenElexSharp -- assets meshes Obj_Bld_Alb_Door_Closed_L1 --properties
dotnet run --project OpenElexSharp -- assets materials AS_AlbSigns_1 --properties
dotnet run --project OpenElexSharp -- assets skeletons CH_Head_F_Ambient_01_L1 --properties
dotnet run --project OpenElexSharp -- assets animations Chest --properties
dotnet run --project OpenElexSharp -- assets blendshapes CH_Head_F_Ambient_01_L1 --properties
dotnet run --project OpenElexSharp -- assets world World --properties
dotnet run --project OpenElexSharp -- assets scene ABE_AbessaCity_Items --limit 1 --properties
dotnet run --project OpenElexSharp -- assets collision Castle --limit 5
dotnet run --project OpenElexSharp -- assets occlusion Palisade --limit 5
dotnet run --project OpenElexSharp -- assets navigation --all --properties
dotnet run --project OpenElexSharp -- assets sounds IKENI7 --limit 5 --properties
dotnet run --project OpenElexSharp -- assets sounds --all
dotnet run --project OpenElexSharp -- assets strings "Worn Journal"
dotnet run --project OpenElexSharp -- assets strings fff85a02
dotnet run --project OpenElexSharp -- assets export data/compiled/localization/en.str out/en.str
```

See [Documentation/Mounts.md](Documentation/Mounts.md), [Documentation/Formats/Pak.md](Documentation/Formats/Pak.md), [Documentation/Formats/Gar5.md](Documentation/Formats/Gar5.md), [Documentation/Formats/Objects.md](Documentation/Formats/Objects.md), [Documentation/Formats/Templates.md](Documentation/Formats/Templates.md), [Documentation/Formats/Textures.md](Documentation/Formats/Textures.md), [Documentation/Formats/Meshes.md](Documentation/Formats/Meshes.md), [Documentation/Formats/Materials.md](Documentation/Formats/Materials.md), [Documentation/Formats/Skeletons.md](Documentation/Formats/Skeletons.md), [Documentation/Formats/Animations.md](Documentation/Formats/Animations.md), [Documentation/Formats/Worlds.md](Documentation/Formats/Worlds.md), [Documentation/Formats/Terrain.md](Documentation/Formats/Terrain.md), [Documentation/Formats/Collision.md](Documentation/Formats/Collision.md), [Documentation/Formats/Audio.md](Documentation/Formats/Audio.md), [Documentation/Formats/Localization.md](Documentation/Formats/Localization.md), [Documentation/Architecture.md](Documentation/Architecture.md), [Documentation/Rendering.md](Documentation/Rendering.md), [Documentation/Lighting.md](Documentation/Lighting.md), [Documentation/Characters.md](Documentation/Characters.md), [Documentation/Levels.md](Documentation/Levels.md), [Documentation/UI.md](Documentation/UI.md) and [Documentation/Assets.md](Documentation/Assets.md).

- Roadmap: [TASKS.md](TASKS.md)
- Workflow, including the review gate before a task is marked complete: [AGENTS.md](AGENTS.md)
- Documentation index: [Documentation/README.md](Documentation/README.md)

## Code metrics

Updated after each completed task by running `Scripts/Get-CodeMetrics.ps1` with `pwsh` or Windows PowerShell. Counts C# source under `OpenElexSharp/` and `OpenElexSharp.Tests/`, excluding `bin/` and `obj/`.

| Scope | Files | Lines | Non-blank LOC | Size |
| --- | ---: | ---: | ---: | ---: |
| Engine | 101 | 21656 | 18918 | 666.5 KB |
| Tests | 77 | 9826 | 8268 | 284.1 KB |
| **Total** | **178** | **31482** | **27186** | **950.6 KB** |

## Credits

- [MonoGame](https://monogame.net) — cross-platform game framework.
- [DigitalRiseModel](https://github.com/rds1983/DigitalRiseModel) — model and animation support on top of MonoGame.
- [Myra](https://github.com/rds1983/Myra) — UI library for MonoGame.
- [BepuPhysics2](https://github.com/bepu/bepuphysics2) — 3D physics engine.
- [DigitalRune](https://github.com/DigitalRune/DigitalRune) — the original model library that DigitalRiseModel derives from.
- [Nursia](https://github.com/Nursia3D/Nursia) (MIT) — terrain patch and skirt reference for seamless terrain rendering; no code copied.

Format-research credits — projects whose source or documentation informed a format specification:

- [rmtools](https://github.com/Baltram/rmtools) (GPL-3.0) — Elex Resource Manager; reference for the G3V0 PAK header, directory tree, file attributes, DEFLATE compression (`mi_genomevolume.cpp`), the Genome object/class model, the embedded class/property name registry, the ELEX image wrapper and its legacy/DXGI texture format tables.
- [GameExtractor](https://github.com/wattostudios/GameExtractor) (GPL-2.0-or-later) — `Plugin_PAK_G3V0_2` independently documents the Risen-family PAK layout reused by ELEX.
- [G3Archive](https://github.com/gekonnn/G3Archive) (MIT) — Gothic 3 PAK header reference that clarified the G3V0 lineage.
- [BCnEncoder.NET](https://github.com/Nominom/BCnEncoder.NET) (MIT OR Unlicense) — CPU decoding for BC6H and BC7 textures.
- [Microsoft DXGI_FORMAT documentation](https://learn.microsoft.com/en-us/windows/win32/api/dxgiformat/ne-dxgiformat-dxgi_format) — authoritative DXGI format identifiers used by the DDS DX10 header.
- [ELEX scripting](https://www.nexusmods.com/elex/mods/64) — community documentation of the `gCInfoCommand*` infoscript data model, used to confirm that ELEX logic is edited as data rather than a shipped script bytecode.

## Legal

ELEX and its assets are © Piranha Bytes and THQ Nordic. This project is not affiliated with or endorsed by them and includes no game content. You must own the game to use it.

Source code is licensed under the [GNU Affero General Public License v3.0](LICENSE).

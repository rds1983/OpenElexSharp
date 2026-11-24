# OpenElexSharp

An open-source, from-scratch C# implementation of the **ELEX** engine and game runtime (Piranha Bytes, 2017), built on MonoGame.

> **Status: pre-alpha.** Asset mounting and asset-format research. There is no playable build yet; the roadmap lives in [TASKS.md](TASKS.md).

OpenElexSharp reads assets from a local ELEX installation. **No game assets or game code are distributed with this project.**

## Technology

| Area | Technology |
| --- | --- |
| Language / runtime | C# 14 on .NET 10 |
| Framework | [MonoGame](https://monogame.net) (DesktopGL) |
| Models & animation | [DigitalRiseModel](https://github.com/rds1983/DigitalRiseModel) |
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
```

`--smoke-test` renders a few frames and exits with code 0 (used for automated verification). At this stage the engine mounts the installation's loose folders from `mount_packed.xml` and opens an empty window; PAK contents and rendering arrive in later tasks. See [Documentation/Mounts.md](Documentation/Mounts.md) for the mount model.

- Roadmap: [TASKS.md](TASKS.md)
- Workflow, including the review gate before a task is marked complete: [AGENTS.md](AGENTS.md)
- Documentation index: [Documentation/README.md](Documentation/README.md)

## Code metrics

Updated after each completed task by running `Scripts/Get-CodeMetrics.ps1` with `pwsh` or Windows PowerShell. Counts C# source under `OpenElexSharp/` and `OpenElexSharp.Tests/`, excluding `bin/` and `obj/`.

| Scope | Files | Lines | Non-blank LOC | Size |
| --- | ---: | ---: | ---: | ---: |
| Engine | 13 | 885 | 743 | 31.8 KB |
| Tests | 8 | 962 | 792 | 29.5 KB |
| **Total** | **21** | **1847** | **1535** | **61.3 KB** |

## Credits

- [MonoGame](https://monogame.net) — cross-platform game framework.
- [DigitalRiseModel](https://github.com/rds1983/DigitalRiseModel) — model and animation support on top of MonoGame.
- [Myra](https://github.com/rds1983/Myra) — UI library for MonoGame.
- [BepuPhysics2](https://github.com/bepu/bepuphysics2) — 3D physics engine.
- [DigitalRune](https://github.com/DigitalRune/DigitalRune) — the original model library that DigitalRiseModel derives from.

Format-research credits are added here whenever another open-source project informs a format specification, together with what it helped with:

_None yet._

## Legal

ELEX and its assets are © Piranha Bytes and THQ Nordic. This project is not affiliated with or endorsed by them and includes no game content. You must own the game to use it.

Source code is licensed under the [GNU Affero General Public License v3.0](LICENSE).

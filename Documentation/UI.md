# HUD and Myra UI

The debug/test HUD is built with [Myra](https://github.com/rds1983/Myra). Layouts live in `.xmmp`
XML, are exported to C# with the locally installed `myra-export` tool, and user logic lives in
hand-written partial classes.

## Layouts

All UI source lives in `OpenElexSharp/UI/`:

| File | Purpose |
| --- | --- |
| `Hud.xmmp` | Root `Panel` stretched over the viewport. |
| `DebugOverlay.xmmp` | Top-left stats block: FPS, draw calls, triangles, meshes. |
| `Controls.xmmp` | Bottom-right control hint; currently `Escape - Exit`. |

Each `.xmmp` uses:

```xml
<Project.ExportOptions Namespace="OpenElexSharp.UI" Class="DebugOverlay" FieldsVisibility="Private" />
```

`myra-export` writes `<Name>.Generated.cs` (tool-owned widget tree and fields) and
`<Name>.cs` **once**. Re-exporting only overwrites `.Generated.cs`; user logic belongs in the
hand-written `.cs` and must never be added to a generated file.

## Exporting

From the UI folder:

```powershell
cd OpenElexSharp/UI
myra-export DebugOverlay.xmmp
myra-export Controls.xmmp
myra-export Hud.xmmp
```

The exporter is installed globally (`myra-export.exe`). The repository does not bundle it.

## Hosting

`OpenElexGame` owns one `Desktop` and sets `MyraEnvironment.Game` in `LoadContent`:

```csharp
MyraEnvironment.Game = this;
_desktop = new Desktop();
_hud = new Hud();
_desktop.Root = _hud;
```

After the 3D or texture scene has been drawn, `Draw` calls `_hud.Update(...)` and then
`_desktop.Render()`. Myra leaves device state behind (scissor rectangle and sampler), so
`Renderer.BeginFrame` saves the state and `Renderer.EndFrame` restores it before the HUD draws.

The stats values come from `Renderer.DrawCalls`, `Renderer.Triangles` and `Renderer.Meshes`.
`DebugOverlay` computes FPS over a half-second window.

## Layout and styling notes

- Myra's 8-digit hex colors are `RRGGBBAA` (alpha last), so `#000000A6` is 65% black.
- `Hud.xmmp` cannot express the final `Stretch` alignment or per-child offsets for the bare root
  `Panel`; those are applied in `Hud.cs`.
- The top-left block is aligned `Left/Top` at `8,8`; the bottom-right block uses `Right/Bottom`
  with negative offsets.
- Myra ships its default skin and `Inter-Regular.ttf` as embedded resources, so no font asset is
  copied into this repository.

## Controls

- `Escape` exits. It is handled by both `Window.KeyDown` and `Game.Update` edge detection.
- The HUD is always visible for now. A toggle key can be added when more debug views exist.

## Related tasks

- T008 stage A — test scene HUD.
- T101 — application shell and debug overlay.

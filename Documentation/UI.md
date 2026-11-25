# HUD and Myra UI

The debug/test HUD is built with [Myra](https://github.com/rds1983/Myra). Layouts live in `.xmmp`
XML, are exported to C# with the locally installed `myra-export` tool, and user logic lives in
hand-written partial classes.

## Layouts

All UI source lives in `OpenElexSharp/UI/`:

| File | Purpose |
| --- | --- |
| `Hud.xmmp` | Root `Panel` stretched over the viewport. |
| `DebugOverlay.xmmp` | Top-left stats block: FPS, update/draw time, memory, draw calls, triangles, drawn/culled parts, batches; mesh-viewer and scene rows. |
| `Controls.xmmp` | Bottom-right control hints: overlay, mesh viewer, culling, fly camera and exit. |

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

The stats values come from `Renderer.DrawCalls`, `Renderer.Triangles` and `Renderer.Meshes`,
plus `FrameStatistics` (FPS, update/draw time, memory). `Renderer.Meshes` counts drawn mesh
**parts**, so a mesh with four submeshes contributes four. FPS is counted over a half-second
window; update/draw values are averages over the last 120 frames; memory shows the process
working set and managed heap. The block refreshes every frame until the first half second has
elapsed so a screenshot taken during start-up still shows values, then twice per second. When the
mesh viewer is active, two more rows show the current resource/catalog position and the active
shading, tangent and culling state; they are hidden otherwise.

## Capturing a frame

Desktop capture can show a black surface for the game window on some multi-GPU setups. Use the
`--capture <path>` option instead: at the requested draw frame the game copies the back buffer to a
PNG and keeps running. `--smoke-test` exits after capture (at least 10 draws); `--capture-frame <n>`
selects the draw frame (default 4), which is useful after shader warm-up or after hiding the debug
overlay.

```powershell
dotnet run --project OpenElexSharp -- --mesh Obj_Bld_Alb_Door_Closed_L1 --smoke-test --capture shot.png
dotnet run --project OpenElexSharp -- --mesh Obj_Dmy_MaterialCube_1_L1 --soak-test 10 --capture later.png --capture-frame 300
```

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
- `F1` toggles the debug stats block; the controls panel lists the key.
- Viewer: `Left`/`Right` cycle meshes, `Home`/`End` jump to the first/last mesh, `V` toggles unlit
  vertex colors, `T` toggles tangent lines and `C` cycles face culling.
- Stress scene: `WASD`/`QE` fly, `Shift` accelerates, right-mouse drag or arrow keys look, `F2`
  toggles frustum culling.
- Sector scenes use the same fly camera and `F2`; `--sector <sec resource>` selects a sector (repeat
  for several).
- The overlay starts visible; hiding it leaves the controls hint in place.

## Related tasks

- T008 stage A — test scene HUD.
- T101 — application shell, logging, crash screen, resize and debug overlay.
- T102 — mesh viewer, vertex colors, tangents and winding resolution.
- T104 — sector loading, transforms, camera framing and fog; see [Levels.md](Levels.md).
- T103 — scene graph, fly camera, culling and batching; see [Rendering.md](Rendering.md).
- See [Architecture.md](Architecture.md) for the host, timestep, logging and soak test.

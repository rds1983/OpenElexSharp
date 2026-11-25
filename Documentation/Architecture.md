# Application shell

`OpenElexSharp` is a MonoGame DesktopGL application. This document covers the host: startup,
logging, crash handling, window/resize behaviour, the fixed timestep and the debug overlay.
Scene and world rendering are covered by their own format documents.

## Startup

`Program.Main` has two paths:

1. **Headless asset inspection** (`assets ...`) — mounts the installation, runs the requested
   inspector and writes machine-readable output to stdout. This path never initializes logging,
   so log lines cannot corrupt piped output.
2. **Game host** — parses `LaunchOptions`, mounts the installation, opens
   `logs/openelexsharp.log`, then runs `OpenElexGame`.

Startup order is: parse arguments → resolve and mount the installation → initialize `Log` →
log the installation, mount summary and asset warnings → run the game. `ELEX_INSTALL_PATH` or
`--elex-path` select the installation (see [Mounts.md](Mounts.md)); the game never embeds an
installation path.

## Graphics device and window

| Setting | Value |
| --- | --- |
| Back buffer | 1280 × 720, `GraphicsProfile.HiDef` |
| Resizable window | yes |
| Vertical sync | on |
| Fixed timestep | 60 Hz (`TargetElapsedTime = 1/60 s`) |
| Inactive sleep | 100 ms (an unfocused window updates at about 10 Hz) |
| Content root | `Content` (not used yet; runtime assets come from the mounted game) |

The device is created by `GraphicsDeviceManager`. `Window.ClientSizeChanged` copies the client
bounds into `PreferredBackBufferWidth/Height` and calls `ApplyChanges`, guarded against
re-entrancy and zero-sized (minimized) windows. On DesktopGL that call raises the device
reset events; the renderer, Myra fonts and textures survive it, and the viewport, camera aspect
ratio and HUD stretch follow the new size. Resize failures are logged and leave the previous
size active.

## Fixed timestep

`OpenElexGame` uses MonoGame's fixed-step loop at 60 Hz and enables vertical sync. Update and
draw time are measured separately with `Stopwatch.GetTimestamp()`; the measured values feed the
debug overlay and the soak log. Update and draw are wrapped in `try/finally`, so a failing frame
still records its timing before the exception reaches the crash handler.

## Logging

`OpenElexSharp.Diagnostics.Log` is the process-wide facade:

- `Log.Info` → `[timestamp] [INFO] message` on stdout and in the file;
- `Log.Warning` / `Log.Error` → `[WARN]` / `[ERROR]` on stderr and in the file;
- `Log.Error(message, exception)` appends the exception's full `ToString()` (including stack).

The default file is `logs/openelexsharp.log` relative to the working directory. `logs/` is
git-ignored. `LogFile` opens the file in append mode, flushes every line, is thread-safe and
moves a file of 5 MiB or more to `.1` when it is opened, so a long-lived development checkout
does not accumulate unbounded logs. `Log.Initialize` falls back to console-only logging when the
file cannot be opened and reports that once.

`LogFile` is an ordinary instance class; the static `Log` facade only adds console output and
lifetime management, which keeps the file behaviour unit-testable.

## Crash handling

`Program` wraps the game loop in a catch-all. On a fatal exception it:

1. writes the exception to the log (`Log.Error`);
2. writes a plain-text report to `logs/crash-yyyyMMdd-HHmmss.txt` via `CrashReport.Save`;
3. prints the report to the console;
4. opens `CrashScreenGame`, a minimal MonoGame window with a scrollable Myra label containing
   the report, the runtime/OS, the log path and the crash-file path;
5. returns exit code **3**.

The crash screen is best-effort. If the graphics stack itself is broken, the report is printed
to stderr instead and the process still exits with code 3. `--crash-test` throws after a few
frames to exercise this path; combined with `--smoke-test` and `--capture <path>` it renders and
saves the crash screen, then closes automatically after two seconds.

Examples of the report:

```text
OpenElexSharp crashed.
Time: 2026-11-25 16:44:51 +07:00
Runtime: .NET 10.0.12
OS: Microsoft Windows 10.0.26200
Log: logs\openelexsharp.log

System.InvalidOperationException: Crash screen test requested by --crash-test.
   at OpenElexSharp.OpenElexGame.UpdateCore(GameTime gameTime) ...
```

## Debug overlay

The HUD is a Myra `Desktop` owned by `OpenElexGame`; see [UI.md](UI.md) for the widget layout.
The top-left stats block shows:

| Row | Source |
| --- | --- |
| `FPS` | frames over the current/last 0.5 s window |
| `Update` | average `Update` time over the last 120 frames |
| `Draw` | average `Draw` time over the last 120 frames |
| `Memory` | process working set and managed heap (`GC.GetTotalMemory(false)`) |
| `Draw calls`, `Triangles`, `Meshes` | `Renderer` counters for the current frame |

`FrameStatistics` owns the windows and the memory probe; `MemorySnapshot` wraps the two byte
counts. Values refresh every frame until the first half second has elapsed (so a capture shows
real numbers immediately) and twice per second afterwards. The overlay is shown by default and
**F1** toggles it; the bottom-right controls panel lists the key. The toggle uses edge detection
in `Update` (the same pattern as Escape) and logs the new state.

`--capture-frame <n>` chooses the draw frame saved by `--capture`, which is useful for screenshots
after shader warm-up or after toggling the overlay. The default remains draw frame 4; a smoke test
waits for the capture frame before exiting.

## Soak testing

`--soak-test <seconds>` runs the normal loop for the requested duration and exits with code 0.
Every 10 s it logs the elapsed time, working set, live managed heap and current FPS; the final
sample is logged as `Soak complete`. Soak samples force a full collection first
(`MemorySnapshot.CaptureCollected`) because the question there is live memory, not garbage
awaiting collection. This is the supported way to check the "runs for minutes" acceptance
criterion without leaving a window open.

## Device loss and reset

`GraphicsDevice.DeviceLost`, `DeviceResetting` and `DeviceReset` are subscribed in
`LoadContent`. A resize triggers a reset, which is logged at `INFO`; the current resources
(textures, effects, Myra font atlases) continue to render. A real device loss sets a flag and the
next update logs the failure and shuts down cleanly instead of drawing with a dead device.
Recreating a lost device in place (and reloading scene resources) is deferred until there are
runtime systems with GPU-side state that need it.

## Tests

The shell logic that does not need a GPU is covered by:

- `LogFileTests` — creation, append, exception text, rotation, disposal, argument validation.
- `CrashReportTests` — report contents, truncation, file naming, missing directories.
- `FrameStatisticsTests` — FPS windows, rolling averages, memory probe, invalid timings.
- `LaunchOptionsTests` — `--soak-test`, `--crash-test`, `--capture-frame` and existing options.

Manual/visual checks recorded for T101: overlay values after warm-up, F1 hides/shows the block,
window resize to 884 × 521 keeps rendering and reflows the HUD, `--crash-test` shows the report
window and exits with code 3, and a 150 s soak keeps the working set flat.

## Related tasks

- T008 stage A — initial Myra HUD and renderer statistics.
- T101 — logging, crash screen, resize, fixed timestep, overlay timings/memory and F1 toggle.

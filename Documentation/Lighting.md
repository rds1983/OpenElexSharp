# Sky, lighting and fog

This document covers the stock `BasicEffect` lighting rig, full-screen sky gradient and per-region
fog used by the level renderer. The scene graph and renderer are described in
[Rendering.md](Rendering.md); sector loading is in [Levels.md](Levels.md).

## Goals

T105 is a first-pass lighting model, not a physically based renderer:

- keep levels readable instead of flat-black or blown out;
- use only stock MonoGame effects (`BasicEffect`, `SpriteBatch`);
- establish sane defaults per ELEX region;
- leave room for real light entities, emissive materials and normal maps in later tasks.

No custom shader is used. Shadows, screen-space effects, local lights and normal mapping are still
out of scope.

## Per-region palettes

`LevelLighting` selects a palette from the loaded sector names. It matches the familiar region
prefix, so `TAV_Fort_Buildings` uses Tavar, `ABE_AbessaCity_Buildings` uses Edan, and so on.

| Region | Sectors | Sky | Fog / horizon | Sun | Ambient |
| --- | --- | --- | --- | --- | --- |
| **Tavar** | `TAV_*` | deep blue to pale sand | warm dust | warm low sun | warm grey |
| **Edan** | `ABE_*`, `EDA_*` | blue to soft blue-white | cool blue-grey | soft warm sun | neutral blue-grey |
| **Ignadon** | `IGN_*` | dark ember to orange | volcanic orange | strong orange sun | warm dark |
| **Xacor** | `XAC_*` | cold blue to pale snow | cold white-blue | cool white sun | bright snow bounce |
| **Default** | tests/unknown | blue-grey | blue-grey | neutral | neutral |

Each palette supplies:

- `SkyTop` and `SkyHorizon` colours;
- `FogColor` (kept equal to the horizon colour so geometry fades into the sky);
- `SunDirection`, `SunColor`;
- `AmbientColor`;
- a small `EmissiveColor` lift so very dark materials do not collapse to pure black;
- `FillDirection` and `FillColor` for a soft second light.

`FogStart` and `FogEnd` still come from the sector radius in `SectorScene`, so large fort/city scenes
keep more depth than small test sectors.

## Sky gradient

`Renderer.DrawSky()` draws a full-screen vertical gradient before the 3D scene:

1. `SkyTopColor` and `SkyHorizonColor` are interpolated into a 1 × 128 texture when either colour
   changes.
2. `SpriteBatch` stretches that texture over the viewport with `BlendState.Opaque`,
   `DepthStencilState.None` and `SamplerState.LinearClamp`, so it always sits behind geometry and
   never writes depth.
3. The 3D rasterizer, depth, blend and sampler states are restored immediately afterwards.

The terrain, buildings and sky share the same horizon colour, so distant geometry fades into the
gradient instead of ending against a hard edge.

## Lighting rig

`Renderer` applies the palette to the stock per-pixel `BasicEffect`:

```text
final colour = texture * (ambient + sun * N·L_sun + fill * N·L_fill) + emissive
```

- One directional sun does most of the shaping.
- One directional fill light lifts the shadowed side.
- Ambient plus a small emissive term keep dark metal and shadowed facades readable.
- `BasicEffect.DirectionalLight2` is disabled.
- Fog is linear and uses the palette's `FogColor`.

The sun direction is shared by the regional palettes so adjacent regions do not suddenly flip the
light when the camera crosses a sector boundary.

## Applying a palette

`SectorScene` selects `LevelLighting.ForSector(...)` from the loaded sector names and exposes it as
`Lighting`. `OpenElexGame.DrawSector()` copies the palette to the renderer before opening the frame:

```text
SkyTopColor, SkyHorizonColor
SunDirection, SunColor
AmbientColor, EmissiveColor
FillDirection, FillColor
FogColor
```

The debug startup log reports the chosen palette (`lighting=Tavar`, `lighting=Edan`, ...).

## Verification

Reference captures at 1280 × 720 with culling on:

| Scene | Lighting | Sky | Draw calls | Triangles | Draw time | FPS |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| `TAV_Fort_Buildings --terrain` | Tavar | blue/sand | 2,617 | 3,866,422 | 8.11 ms | 60 |
| `ABE_AbessaCity_Buildings --terrain` | Edan | blue/blue-white | 1,669 | 3,301,616 | 6.30 ms | 60 |
| `IGN_East_Buildings --terrain` | Ignadon | ember/orange | 3,510 | 4,762,253 | 25.6 ms | 46 |
| `XAC_Buildings --terrain` | Xacor | cold blue | 6,267 | 8,873,662 | 19.8 ms | 58–60 |

The TAV and Abessa reference scenes read clearly and are neither black nor blown out. The Ignadon
and Xacor sectors are much heavier and still contain very dark metal/missing-light materials; local
lights and emissive materials are T108, and per-material lighting is T401.

## Limitations

- No local/point lights, emissive meshes or lightmaps yet.
- No normal maps or specular response; materials are diffuse textures.
- No shadows, ambient occlusion or tone mapping.
- The sky is a gradient, not a textured skybox or sun disc.
- Time of day and weather are fixed per region.
- The palettes are authored approximations, not decoded from level data.

## Tests

- `OpenElexSharp.Tests/LevelLightingTests.cs` — region selection, fog/horizon consistency, normalized
  sun and fill directions.
- Rendering and sky behaviour are verified against the retail installation with captured frames.

## Related tasks

- T105 — sky and lighting approximation (this task).
- T108 — world visuals: light entities, emissive materials, decals and water.
- T401 — material flags, normal maps and per-material lighting.
- T406 — LOD and dense-scene performance.

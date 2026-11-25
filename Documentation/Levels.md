# Sector rendering

This document covers loading static scene sectors (`sec`) at runtime and drawing them with the
scene graph, frustum culling and fog. Format details are in [Formats/Worlds.md](Formats/Worlds.md);
rendering infrastructure is in [Rendering.md](Rendering.md).

## Command line

```powershell
dotnet run --project OpenElexSharp -- --sector ABE_AbessaCity_Buildings
dotnet run --project OpenElexSharp -- --sector TAV_Fort_Buildings
dotnet run --project OpenElexSharp -- --sector ABE_AbessaCity_Buildings --sector ABE_AbessaCity_Decoration
```

`--sector` may be repeated; names are matched exactly first and then as substrings. The first
matching sector wins for a partial name. `--no-cull` starts with frustum culling off and `F2`
toggles it. The camera is the T103 fly camera; `WASD`/`QE` move, `Shift` accelerates and right-mouse
drag or the arrow keys look.

## Loading

For each requested sector `SectorScene`:

1. resolves the `sec` resource through the `RT02` index and parses it with `ElexScene`;
2. walks the `eCDynamicEntity` tree and creates one `SceneNode` per entity;
3. converts the row-major `bCMatrix` local transform with `SceneTransforms.ToXna`;
4. resolves `MeshFileName` to an `msh` resource, loads its `eCMeshResource2` metadata and uploads
   the mesh once per unique name;
5. shares one `MaterialLibrary` across all meshes, so textures used by many buildings are uploaded
   once;
6. composes the hierarchy (`world = local * parentWorld`) and verifies the result against the
   already-composed `ElexSceneEntity.WorldTransform`.

Entities without a mesh (groups, NPC placeholders, triggers) become transform-only nodes. Missing
meshes, failed metadata, non-finite transforms and transform mismatches are collected as warnings
and counted in the startup log/overlay; the tested building sectors load with **0 missing meshes
and 0 transform mismatches**.

## Camera and fog

The camera framing uses the 90th percentile of mesh-instance center distances from their average
center: distant outlier props do not drag the view away from the main location. The resulting
bounds give the camera position, near/far planes, movement speed and fog range:

```text
fogStart = max(1000, radius * 0.9)
fogEnd   = max(fogStart + 1000, radius * 2.8)
```

Fog uses `BasicEffect` linear fog with a muted blue-grey colour; the back buffer is cleared to the
same colour, which stands in for a sky until T105. Fog is disabled in the mesh viewer and stress
scene.

## Measured sectors

Reference installation, 1280 × 720, 60 Hz, culling on, camera framed by the percentile rule:

| Sector | Entities | Instances | Parts | Unique meshes | Missing | Transform mismatches | Draw calls | Triangles | Draw time | FPS |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `ABE_AbessaCity_Buildings` | 750 | 748 | 1,610 | 98 | 0 | 0 | 1,598 | 667,568 | 4.36 ms | 60 |
| `TAV_Fort_Buildings` | 1,304 | 1,301 | 2,633 | 166 | 0 | 0 | 2,537 | 897,814 | 8.62 ms | 60 |
| Buildings + Decoration | 5,776 | 5,773 | 7,802 | 489 | 0 | 0 | 7,801 | 5,114,707 | 35.6 ms (start-up) | 13 |

The two-sector run is supported but not optimized: it demonstrates why T406's instancing/static
merging and LOD selection matter for dense sectors. The single-sector runs are the T104 reference
and stay at 60 FPS after shader warm-up.

Draw calls remain per mesh part; culling removes parts outside the view but the sector is loaded as
individual entities. The T103 static-chunk strategy is available for future per-sector merging.

## Visual state

- Textures, UVs, normals and materials render through the T102 path.
- Some alpha-test foliage, wires and decal meshes render as opaque dark geometry because material
  alpha/culling flags are not decoded yet (T401/T108). They do not create missing-mesh gaps.
- Terrain (`terrain3.trn`) is not loaded yet; the sectors float in fog. Terrain parsing and
  rendering are a separate format task.
- Lighting is the fixed BasicEffect rig from T008; T105 replaces it with a level-appropriate sun,
  ambient and sky.

## Tests

- `SceneTransformsTests` — row-major `Matrix4x4` to XNA matrix conversion and translation.
- `LaunchOptionsTests` — single/repeated `--sector` parsing and missing values.
- Sector loading, transform validation and performance are verified against the retail
  installation and captured screenshots; they need a GPU and game data.

## Related tasks

- T104 — sector loading, transforms, fog and camera framing.
- T105 — sky and level lighting.
- T406 — LOD, instancing and dense-scene performance.
- T401/T108 — material alpha/two-sided flags and foliage/decal visuals.

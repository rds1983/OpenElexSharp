# Sector rendering

This document covers loading static scene sectors (`sec`) at runtime and drawing them with the
scene graph, frustum culling and fog. Format details are in [Formats/Worlds.md](Formats/Worlds.md);
rendering infrastructure is in [Rendering.md](Rendering.md).

## Command line

```powershell
dotnet run --project OpenElexSharp -- --sector ABE_AbessaCity_Buildings
dotnet run --project OpenElexSharp -- --sector TAV_Fort_Buildings --terrain
dotnet run --project OpenElexSharp -- --sector ABE_AbessaCity_Buildings --visuals
dotnet run --project OpenElexSharp -- --sector ABE_AbessaCity_Buildings --sector ABE_AbessaCity_Decoration
```

`--sector` may be repeated; names are matched exactly first and then as substrings. The first
matching sector wins for a partial name. `--no-cull` starts with frustum culling off and `F2`
toggles it. `--terrain` loads heightmap tiles around the sector bounds (see below). `--visuals`
loads nearby companion sectors for lights, decals, water and vegetation. The camera is the T103 fly
camera; `WASD`/`QE` move, `Shift` accelerates and right-mouse drag or the arrow keys look.

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

Fog uses `BasicEffect` linear fog with the region palette's horizon colour; the sky gradient uses the
same colour at the horizon, so distant geometry fades into the background. `SectorScene` selects a
`LevelLighting` palette from the sector names and `OpenElexGame` applies its sky, sun, ambient and fog
values to the renderer. See [Lighting.md](Lighting.md). Fog is disabled in the mesh viewer and stress
scene.

## Terrain

`--terrain` reads the heightmaps embedded in the per-tile static-scene blobs. The format is described
in [Formats/Terrain.md](Formats/Terrain.md). `TerrainScene`:

1. converts the sector bounds center to a 3200-unit tile index;
2. loads every existing tile in a radius of 1–4 tiles (the radius grows with the sector footprint and
   is clamped);
3. parses each blob with `ElexTerrainChunk` (transposing the stored column-major grid), averages
   every shared perimeter sample across neighbouring tiles, blends the correction over a 32-sample
   band, and builds one 137 × 137 `DrMesh` per tile with positions, normals sampled from the stitched
   global grid, and 8 × 8 texture repeats;
4. shares one `MaterialLibrary` and one terrain material (`TR_Edan_Grass_01`) across all tiles;
5. submits each tile as its own mesh, so frustum culling works per chunk.

The raw sample-to-world formula is `y = minY + sample * 3.125`. Because the absolute height does not
line up with entity Y in every region, the scene also applies one constant vertical offset so the
average terrain height matches the sector's estimated ground. `SectorScene` estimates that ground as
the 10th percentile of mesh-box bottoms. The offset is logged (`offset=`) and is an approximation
until the per-region height base is understood.

Stitching makes shared tile edges use identical positions and normals, so the rendered surface is
continuous. A 200-unit skirt is added only at the outer boundary of the loaded block, where there is
no neighbouring tile to stitch against.

A TAV fort run loads 81 chunks at radius 4, renders 2,617 draw calls / 3.87M triangles and holds
60 FPS. Some outer tiles contain high 1–2 sample border bands; stitching averages them with their
neighbours, which can form steep ridges. See the terrain spec's open questions.

Terrain currently uses one texture for all biomes: material splatting and terrain holes are not
decoded yet.

## World visuals (`--visuals`)

The static building sectors are only part of a location. `--visuals` resolves the companion
sectors for the requested sector prefix and loads the entities that fall near the base scene:

| Category | Source | Runtime representation |
| --- | --- | --- |
| Lights | `eCPointLight_PS` entities and `Obj_LS_*` / light-source meshes | Source meshes; point-light properties are not applied to `BasicEffect` yet. |
| Decals | `eCDecal2_PS` entities | One textured, double-sided alpha quad per decal using `Material`, `Extends`, `Offset` and `Rotation`. |
| Water | `eCMesh_PS` with `GameMapType_Water`, `eCTerrainRiver_PS` | Water/lava meshes, drawn with alpha blending and a cool tint. Terrain-river spline nodes are counted but do not generate a surface yet. |
| Vegetation | `_Nature` mesh entities and `eCSpeedTreeStatic_PS` | Static nature meshes; SpeedTree `spt` resources are still only partially decoded (see limitations). |

Companion names are derived from the requested sector by stripping `_Buildings`, `_Decoration`,
`_Nature`, `_SpeedTree`, `_Speedtree`, `_SpeedTrees`, `_Lights` or `_Rivers` and then trying the
prefix and region variants (`ABE_AbessaCity_Nature`, `ABE_Lights`, `ABE_Rivers`, ...). Exact
resources that exist are loaded; missing companions are skipped.

Companion entities are filtered to an expanded sphere around the base scene bounds (at least a
2,000-unit radius, 1.25 × the base radius + 1,500 units). This keeps region-wide light and river
sectors from dragging the camera or inflating memory with distant entities. The base sector still
controls camera framing, fog and ground level.

Debug keys while a sector is loaded:

| Key | Category |
| --- | --- |
| `1` | Lights |
| `2` | Decals |
| `3` | Water |
| `4` | Vegetation |
| `0` | Show all categories |

The overlay scene row reports `lights=on/333 decals=on/9 water=on/19 veg=on/367`. Toggling hides only
the registered mesh nodes; building and terrain geometry stays visible.

`ABE_AbessaCity_Buildings --visuals` loads 2,193 mesh instances / 3,504 parts with 333 light nodes,
9 decals, 19 water nodes and 367 vegetation nodes. It renders at 58–60 FPS after shader warm-up with
about 1.35 GB working set. Lights dominate the memory cost because their region sector is large and
the current loader keeps decoded source meshes for nearby entities.

## Measured sectors

Reference installation, 1280 × 720, 60 Hz, culling on, camera framed by the percentile rule:

| Sector | Entities | Instances | Parts | Unique meshes | Missing | Transform mismatches | Draw calls | Triangles | Draw time | FPS |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `ABE_AbessaCity_Buildings` | 750 | 748 | 1,610 | 98 | 0 | 0 | 1,598 | 667,568 | 4.36 ms | 60 |
| `TAV_Fort_Buildings` | 1,304 | 1,301 | 2,633 | 166 | 0 | 0 | 2,537 | 897,814 | 8.62 ms | 60 |
| `TAV_Fort_Buildings --terrain` | 1,304 | 1,301 | 2,633 | 166 | 0 | 0 | 2,617 | 3,866,422 | 8.11 ms | 60 |
| `ABE_AbessaCity_Buildings --terrain` | 750 | 748 | 1,610 | 98 | 0 | 0 | 1,669 | 3,301,616 | 6.30 ms | 60 |
| Buildings + Decoration | 5,776 | 5,773 | 7,802 | 489 | 0 | 0 | 7,801 | 5,114,707 | 35.6 ms (start-up) | 13 |

The two-sector run is supported but not optimized: it demonstrates why T406's instancing/static
merging and LOD selection matter for dense sectors. The single-sector runs are the T104 reference
and stay at 60 FPS after shader warm-up.

Draw calls remain per mesh part; culling removes parts outside the view but the sector is loaded as
individual entities. The T103 static-chunk strategy is available for future per-sector merging.

## Visual state

- Textures, UVs, normals and materials render through the T102 path.
- `--visuals` adds nearby light-source meshes, real decal quads, water/lava meshes and nature
  geometry, with independent category toggles.
- Alpha-test foliage, wires and non-decal materials still render as opaque dark geometry because
  material alpha/culling flags are not decoded yet (T401). They do not create missing-mesh gaps.
- SpeedTree `spt` files contain composite GMSH headers before the renderable mesh. The loader tries
  every GMSH block and keeps the largest parseable mesh, but most trees still fall back to an
  untextured material and some variants do not produce a mesh. Full SpeedTree material/LOD decoding
  remains open.
- Point-light `eCPointLight_PS` properties (colour, range, intensity) are not applied to the stock
  `BasicEffect`; only the light-source meshes are rendered and toggled.
- Terrain-river `eCTerrainRiver_PS` nodes are counted as water but do not generate a spline surface
  yet; water planes and lava-pond meshes do render.
- Terrain is loaded only with `--terrain`; it uses one grass material for all regions and an
  approximate vertical alignment. Terrain splatting, holes and map-edge border bands remain open
  (T109/T401).
- Lighting uses a per-region stock `BasicEffect` palette (sky gradient, sun, fill, ambient, emissive)
  selected from the sector names; see [Lighting.md](Lighting.md). Ignadon/Xacor still contain very
  dark metal materials that need local lights/emissive materials (T108 follow-up/T401).

## Tests

- `SceneTransformsTests` — row-major `Matrix4x4` to XNA matrix conversion and translation.
- `ElexTerrainChunkTests` - synthetic terrain headers, column-major transpose, height mapping, truncation and corrupt input.
- `TerrainSceneTests` - chunk index rounding, blob path formatting and shared-edge/corner stitching with blending.
- `LaunchOptionsTests` — single/repeated `--sector`/`--terrain`/`--visuals` parsing and missing values.
- `LevelLightingTests` — region palette selection, fog/horizon consistency and normalized light directions.
- `SectorEntityClassifierTests` — light, decal, water, vegetation and rock classification.
- `SectorCompanionsTests` — companion name derivation for city, fort, XAC and speedtree sectors.
- `SectorVisualStateTests` — category registration, toggles, counts and overlay summary.
- Sector loading, transform validation and performance are verified against the retail
  installation and captured screenshots; they need a GPU and game data.

## Related tasks

- T104 — sector loading, transforms, fog and camera framing.
- T105 — sky and level lighting.
- T108 — light/decals/water/vegetation companion sectors and debug category toggles.
- T406 — LOD, instancing and dense-scene performance.
- T401 — material alpha/two-sided flags and per-material lighting.

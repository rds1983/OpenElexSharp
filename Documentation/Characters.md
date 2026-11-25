# Character rendering

This document covers the first playable-character rendering path: loading an `skn` resource, its
embedded bind-pose skeleton and material submeshes, and drawing the mesh with MonoGame's stock
`SkinnedEffect`. The file format is in [Formats/Skeletons.md](Formats/Skeletons.md); materials and
meshes are in [Formats/Meshes.md](Formats/Meshes.md) and [Formats/Materials.md](Formats/Materials.md).

## Command line

```powershell
dotnet run --project OpenElexSharp -- --character Mesh_Male_FullBody_Alb_High_01_L1
dotnet run --project OpenElexSharp -- --character Mesh_Cre_Mon_Cavebird_01_L1
dotnet run --project OpenElexSharp -- --character Mesh_Cre_Drone_DroneRobot_01_L1
dotnet run --project OpenElexSharp -- --character CH_Head_F_Ambient_01_L1
```

`--character` accepts an exact name or a substring. The viewer slowly orbits the model, shows the
bind pose and reports bone/material counts in the debug overlay. `--capture <png>` and
`--capture-frame <n>` work as in the other scenes.

## Loading pipeline

1. Resolve the `skn` resource and parse it with `GmshMesh.ParseSkinned`. Unlike a static `msh`,
   `header[0]` points at the skeleton tail, so the GPU index/vertex counts come from the two buffer
   descriptors.
2. Decode the skin stream (usage 5): four `uint8` bone indices and four half-float weights per
   vertex. Missing/zero weights are replaced with one influence on bone 0; all weights are
   renormalized after half-float quantization.
3. Parse the embedded skeleton tail with `Skeleton.TryParse`.
4. Read `eCMotionSkinResource2` metadata, resolve material names through `MaterialLibrary`, and split
   the index buffer into one `DrMeshPart` per submesh.
5. Upload one `VertexPositionNormalTextureSkinned` buffer shared by all parts and one 16/32-bit index
   buffer.
6. Render the bind pose with identity bone transforms. The inverse bind matrices are calculated and
   kept for T107 animation playback.

## `SkinnedEffect` path

`Renderer` owns a `SkinnedEffect` alongside its `BasicEffect` mesh path:

- `WeightsPerVertex = 4`, `PreferPerPixelLighting = true`;
- the same sun/fill/ambient/emissive and fog values as the world renderer;
- one draw per material submesh, so multi-material bodies work;
- `SetBoneTransforms` receives one matrix per skeleton bone.

The stock effect is sufficient for the retail budgets: the skeleton parser observes at most 65
bones, while `SkinnedEffect.MaxBones` is 72 and the vertex format carries four influences.

## Bind-pose coverage

| Resource | Bones | Parts/materials | Result |
| --- | ---: | ---: | --- |
| `Mesh_Male_FullBody_Alb_High_01_L1` | 61 | 2 | Body renders correctly at 60 FPS |
| `CH_Head_F_Ambient_01_L1` | 6 | 4 | Head/hair/eyes render correctly |
| `Mesh_Cre_Mon_Cavebird_01_L1` | 30 | 2 | Creature renders correctly |
| `Mesh_Cre_Mon_Biter_01_L1` | 55 | 1 | Creature renders in bind pose; no bind transforms for animation |
| `Mesh_Cre_Drone_DroneRobot_01_L1` | 10 | 1 | Animated prop renders correctly |

The bind-pose mesh is authored correctly even for skeletons whose tails contain names/parents but no
bind transforms; those resources can be displayed, but cannot be animated until the bind records are
decoded.

## Limitations

- Bind pose only; animation sampling/playback is T107.
- Equipment attachments are separate `skn` resources and are not yet parented to body bones.
- The 50 unrecognized skeleton tails may fail to load.
- Material alpha/culling flags and normal maps remain T401; character materials use diffuse textures.
- No shadows, local lights, blend shapes or facial animation.
- The viewer uses a simple orbit camera, not a gameplay character controller.

## Tests

- `OpenElexSharp.Tests/GmshMeshTests.cs` — skinned GMSH descriptor counts, usage-5 bone indices and
  weights, format sizes.
- `OpenElexSharp.Tests/SkinnedMeshTests.cs` — weight normalization, zero-weight fallback and bone
  index clamping.
- `OpenElexSharp.Tests/LaunchOptionsTests.cs` — `--character` parsing.
- Retail resources are verified with the character viewer and captured frames (male body, female
  head, cavebird, biter, drone).

## Related tasks

- T106 — character bind-pose rendering (this task).
- T107 — animation playback and crossfades.
- T401 — material flags, normal maps and alpha-tested character parts.
- T108 — world lights and emissive character materials.

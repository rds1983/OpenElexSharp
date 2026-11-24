# Worlds and scene sectors (`wrl` / `sec`)

ELEX world placement is split between a small world definition (`wrl`) and per-sector static scene
resources (`sec`). A world lists its sectors in order; each sector contains an entity tree with local
transforms, names, GUIDs, property sets and links to meshes, templates and animation networks.

## Purpose and where they appear

| Type | Class | Count | Contents |
| --- | --- | ---: | --- |
| `wrl` | `class gCTerrain` | 4 | Terrain resource path and the ordered scene-sector list. |
| `sec` | `class gCEmbeddedLayer` / `class eCScene` | 481 | Static scene entities, transforms and per-entity property sets. |

Logical names are resolved through the `w_*_na.db` resource databases (`w_wrl_0_na.db`,
`w_sec_0_na.db`). `World` is the main world; `CombatTest`, `Testlevel` and `DialogTagger` are test or
editing worlds.

## Container

Both resource types use nested GAR5 wrappers and a GEC2 object:

```text
GAR5                        outer resource wrapper (44 bytes observed)
  uint32 0x20
  byte[36] zero/resource metadata
GAR5                        inner wrapper
  uint32 0x20
  [wrl]  GEC2 gCTerrain
  [sec]  uint16 1
         uint16 resourceNameLength
         char[] resourceName       (Latin-1, for example ABE_AbessaCity_Items)
         GEC2 gCEmbeddedLayer
```

The inner resource name is present in `sec` resources and absent in `wrl`. Readers should locate the
first valid `GEC2` object after the outer wrapper instead of relying on a fixed offset.

## World definition (`wrl`)

The GEC2 object has class hash `0x207A4D1A` (`class gCTerrain`, version 2). It has one property:

| Property type | Property name | Data |
| --- | --- | --- |
| `class bCString` (`0xA83E6117`) | `FileName` (`0xAB93AA26`) | `uint16` length followed by Latin-1 bytes. |

After the object comes the sector list:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint16` | Sector count. |
| 2 | `Name[]` | `uint16` length + Latin-1 name for each sector. |

`World.wrl` contains 436 sectors and the terrain path
`#G3:/data/raw/terrain3/terrain3.trn`. The first entries include `SysDyn_{...}` and
`World_Test_Zone_*`; regular entries include `ABE_Items`, `EDA_NPCs` and `World_Occluders`. A 36-byte
trailer follows the names (unresolved).

Sector names resolve through the resource index with type token `sec`; the list order is the world
order used by the game.

## Scene sector (`sec`)

The inner object is `class gCEmbeddedLayer` (`0x7A99504C`, version 1). Its class data starts with an
8-byte shim (`A9 31 DA 34 00 00 00 00`) followed by an `eCScene` GEC2 object
(`0xFB8237D1`, version 2).

The `eCScene` class data starts with a `uint32` value (observed `1`–`3`, purpose unknown) followed by
one `eCDynamicEntity` GEC2 object. That object spans the rest of the class data and contains the
complete entity tree.

### Entity object

An entity is `class eCDynamicEntity` (`0x36182D05`, version 9) with two class-data blocks:

| Class data | Version | Layout |
| --- | ---: | --- |
| `class eCDynamicEntity` | 9 | Leading 64-byte `bCMatrix` local transform, then 133 bytes of unresolved data. |
| `class eCEntity` | 4 | Entity identity, property sets and children. |

`eCEntity` layout:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `Guid` | Entity GUID. |
| 16 | `uint16` | Name length; `0` for the scene root. |
| 18 | `char[]` | Latin-1 entity name. |
| 18 + nameLength | `byte[6]` | Unresolved fixed data (see open questions). |
| … | `uint8` | Property-set count. |
| … | `GEC2[]` | That many property-set objects. |
| … | `uint32` | Child count. |
| … | `GEC2[]` | That many child `eCDynamicEntity` objects, recursively. |

Each property-set object is a normal GEC2 object with its own class hash and properties; the existing
Genome property decoder applies.

### Transforms

The leading `bCMatrix` is a row-major 4×4 matrix of `float32` values and uses the row-vector
convention. Translation is stored at indices 12–14 (`M41`–`M43`). Composing a child with its parent is:

```text
world = local × parentWorld
```

Verified on nested `FP_Stand` entities: a parent at `(-165082, 61155, 83215)` and a child local offset
of `(165.2, 0, -12.3)` yields the expected world position `(-164917, 61155, 83203)`. Rotation and scale
are obtained with `Matrix4x4.Decompose`; mirrored placements produce negative scale (for example the
trapdoor at `(-2.461, 2.487, -2.461)`). Units are not confirmed; retail coordinates are in the
hundred-thousands.

### Entity links

| Link | Property set | Property | Data |
| --- | --- | --- | --- |
| Mesh | `class eCMesh_PS` (`0x1CB652B2`) | `MeshFileName` (`0x0296D7B3`) | `bCString` mesh resource name. |
| Template | `class gCItem_PS` (`0xF3C909F6`) | `ItemInventory` (`0xBE2D6DC2`) | 16-byte `eCTemplateEntityProxy` GUID. |
| Template | `class gCRecipe_PS` (`0xA8AE257F`) | `ResultItem` (`0xCA639773`) | 16-byte `eCTemplateEntityProxy` GUID. |
| Animation network | `class eCAnimation3Controller_PS` (`0xD256AADC`) | `NetworkResource` (`0xE186AC77`) | `eTResourceProxy` string, for example `Male`. |
| Collision | `class gCLadder_PS` | `Mesh` | `eTResourceProxy` mesh name. |
| Entity/template proxies | any property set | various | `class eCEntityProxy`, `class eCTemplateEntityProxy`, `class bCGuid`. |

Static decorations, buildings and items carry an `eCMesh_PS` directly, so no separate template lookup
is needed. NPCs carry no mesh; they reference an animation network (`Male`) and their visual is chosen
from `gCNPC_PS` species/gender data at runtime. Some inventory and recipe entries reference item
templates by GUID.

## Evidence from the retail installation

| Metric | Value |
| --- | ---: |
| `wrl` resources | 4 |
| `World.wrl` sectors | 436 |
| `sec` resources | 481 |
| Total scene entities | 376,186 (304,149 with meshes) |
| `ABE_AbessaCity_Items` entities | 18 (17 with meshes) |
| `ABE_AbessaCity_NPCs` entities | 22 (all `Male`) |
| `XAC_Interacts` entities | 245 (244 with meshes, depth up to 2) |
| `ABE_Interacts_Creatures` entities | 486 (485 with meshes) |
| `World.wrl` terrain file | `#G3:/data/raw/terrain3/terrain3.trn` |

Small hex excerpt from the start of an entity name (`Obj_Dec_Bsk_TrapDoor_1`):

```text
0F 00 4F 62 6A 5F 44 65 63 5F 42 73 6B 5F 54 72 61 70 44 6F 6F 72 5F 31
```

## Open questions

- Meaning of the 6-byte fixed field after the entity name. Observed values include
  `9D A5 00 00 01 00`, `1D 04 00 00 01 00` and `9D 85 00 00 01 00`; it is probably flags plus a
  visibility/layer value.
- Meaning of the 133-byte `eCDynamicEntity` tail after the local matrix.
- Meaning of the `uint32` before the root entity in `eCScene` class data (observed `1`–`3`).
- The 36-byte trailer after the `wrl` sector list.
- World units and whether the matrix is metres or engine units.
- NPC visual selection: `gCNPC_PS` species/gender is decoded but not yet resolved to a template/mesh.
- How `sec` resources not listed in `World.wrl` (test/combat scenes) are selected at runtime.
- The referenced `.trn` terrain resource is under `data/raw`, not in the compiled asset catalog.

## Implementation status

| Component | File |
| --- | --- |
| World definition parser | `OpenElexSharp/Assets/ElexWorld.cs` |
| Scene-sector and entity parser | `OpenElexSharp/Assets/ElexScene.cs` |
| CLI inspectors | `OpenElexSharp/Assets/AssetCatalogConsole.cs` (`assets world`, `assets scene`) |
| Tests | `OpenElexSharp.Tests/ElexWorldTests.cs`, `ElexSceneTests.cs` |

Commands:

```powershell
dotnet run --project OpenElexSharp -- assets world World --properties
dotnet run --project OpenElexSharp -- assets scene ABE_AbessaCity_Items --limit 1 --properties
dotnet run --project OpenElexSharp -- assets scene XAC_Interacts --limit 1 --properties
```

`world` prints name, sector count, terrain path and source; `--properties` lists every sector name.
`scene` prints name, entity count, mesh count and source; `--properties` prints one tab-separated row
per entity with depth, name, world position, world rotation quaternion, mesh, template GUID,
animation network and a transform flag, followed by decoded GUID/resource references. `EDA_Decals`
contains entities whose matrix has non-finite components; the parser replaces those components with
zero, keeps the entity in the graph and marks the transform as `non-finite`.

## Related tasks

- T006 — templates referenced by item/recipe entities.
- T010 — this specification and parser.
- T011 — collision data (`col` resources, collision shape lists).
- T102/T106 — spawning scene entities with meshes.

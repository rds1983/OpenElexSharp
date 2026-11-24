# Materials (eCMaterialResource2)

ELEX materials use the `mat` type (`.elexmat`) and resolve through `w_mat_*.db` databases to hashed
`.rom` files. A material resource references image resources by name; the owning database record
stores the shader instance name. Static meshes bind materials through their
[`eCSubMesh.MaterialName`](Meshes.md#mesh-metadata-materials-and-lods).

## Purpose and scope

This specification covers the GAR5-wrapped material payload, the template name, the texture-entry
table and the database-side shader name. Mesh geometry and submesh ranges are documented in
[Meshes.md](Meshes.md). Texture payloads and DDS decoding are documented in
[Textures.md](Textures.md).

## Container layout

The `.rom` starts with the 44-byte GAR5 image-style wrapper used by other compiled resources, then a
second GAR5 header and the material instance:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `char[4]` | Outer magic `GAR5`. |
| 4 | `uint32` | Header value `0x20`. |
| 8 | `byte[36]` | Zero/metadata bytes. |
| 44 | `char[4]` | Inner magic `GAR5`. |
| 48 | `uint32` | Header value `0x20`. |
| 52 | `uint32` | Unresolved header value; observed `1`, `3`, `4`, `11`. |
| 56 | `uint8` | Observed `0`. |
| 57 | `uint16` | Template-name length. |
| 59 | `char[]` | Template name, for example `default`, `Small` or `Water_Plane_Fast_Big`. |
| ... | variable | Material parameter blocks (not decoded). |
| ... | entries | Texture-entry table (see below). |

The header value at offset 52 is **not** a layout version: materials with different values use the
same layout. It is preserved as an unresolved field.

The parameter data between the template name and the texture table has variable size, so the table
does **not** start at a fixed offset (observed at `0x5E` through `0x185`). To locate it, scan for the
first texture-entry pattern described below.

## Texture entries

A texture entry is a length-prefixed name record:

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0 | `uint32` | Declared size; always `nameLength + 4`. |
| 4 | `uint32` | Name length in bytes. |
| 8 | `char[nameLength]` | Latin-1 image resource name. |
| 8 + nameLength | `uint32` | Observed `1`. |
| 12 + nameLength | `uint32` | Observed `0`. |
| 16 + nameLength | `byte[16]` | Resource GUID. |
| 32 + nameLength | `uint32` | Additional resource value. |

The declared-size field makes the entries recognizable. Materials can contain several parameter
blocks and several texture tables (multi-pass or layered materials), so entries may appear in more
than one group. The loader collects entries in payload order and de-duplicates by name. The 28-byte
metadata block is preserved but not interpreted; some entries (notably the last specular slot) carry
a different parameter layout after the name, which is why the loader keys on the declared size and
the name pattern rather than on fixed metadata.

## Texture roles

Roles are inferred from the image resource-name suffix:

| Suffix | Role |
| --- | --- |
| `_Df`, `_DfA` | Diffuse/albedo (with alpha). |
| `_Nm` | Normal map. |
| `_Sm` | Specular. |
| `_Sm2` | Secondary specular. |
| `_Em` | Emissive. |
| `_Co` | Color. |
| other (`_FxG`, `_Fx2`, `_AO`, …) | Effect gradients, masks and other auxiliary maps. |

The primary surface texture is the first diffuse map, otherwise the first non-normal texture,
otherwise the first entry. Materials without any texture entry (for example
`AS_Alb_MirrorBacklight_1`) are untextured and render with the scene fallback texture.

## Database metadata

The `w_mat_*.db` record's `GEC2` class block is `class eCMaterialResource2` version 2 with one
property:

| Property | Type | Meaning |
| --- | --- | --- |
| `InstanceMaterial` | `class bCString` | Shader instance name, for example `st_DfANmSmSm2.shader`. |

The shader name describes the texture slot set (`Df`, `DfA`, `Nm`, `Sm`, `Sm2`, `Co`), but the
payload's own texture names and suffixes are authoritative for binding.

## Runtime representation

`ElexMaterial` is both the parsed payload and the runtime material: `MaterialLibrary` attaches the
database shader name (`Shader`) and the uploaded primary texture (`Texture`) after parsing. The
current renderer binds only `Texture`; the other texture roles remain available as names for later
passes.

## Resolution chain

```text
GMSH mesh
  -> w_msh DB class block (eCMeshResource2)
  -> SubMeshes[i].MaterialName
  -> w_mat resource (mat)
  -> w_mat DB class block (InstanceMaterial)
  -> material ROM (texture entries)
  -> w_img resources (img)
  -> DDS payload (Textures.md)
```

## Observed retail set

| Metric | Value |
| --- | ---: |
| `mat` resources | 1,726 |
| Parsed material payloads | 1,726 |
| Materials without texture entries | 319 |
| Most common texture count | 4 (629 materials) |
| Maximum texture entries | 76 (`Obj_SubstanceSphere`, multi-pass) |
| Distinct suffixes observed | `_Df`, `_DfA`, `_Nm`, `_Sm`, `_Sm2`, `_Em`, `_Co`, `_FxG`, `_Fx2`, `_AO`, … |

## Evidence

`AS_AlbSigns_1`:

```text
inner header: GAR5 0x20 0x00000001 00 0700 "default"
texture entries (offset 0x84):
  AS_AlbSigns_1_DfA
  AS_AlbSigns_1_Sm
  AS_AlbSigns_1_Nm
  AS_AlbSigns_1_Sm2
database: InstanceMaterial = st_DfANmCo.shader
```

`VFX_Pipe_Ice_1` shows the variable header: template name `Small`, first entry at `0x5E`, and two
`VFX_Tileable_Dust_Df` entries.

`AS_Alb_Wall_1` contains two texture tables (two passes) and the auxiliary entry
`TR_Ice_HeightMask_Nm`.

## Open questions

- Meaning of the material parameter blocks between the template name and the texture table.
- Meaning of the header value at offset 52.
- Semantics of the 28-byte texture metadata (GUID, flags and the extra value).
- How multi-pass/layered materials select a pass at runtime.
- Whether shader parameters (colors, scalars, textures) are serialized as typed properties.
- Exact shader-instance naming rules beyond the observed `st_<slots>.shader` pattern.

## Implementation

| Component | File |
| --- | --- |
| Material payload parser and runtime material | `OpenElexSharp/Assets/ElexMaterial.cs` |
| Database shader metadata | `OpenElexSharp/Assets/MaterialMetadata.cs` |
| RT02 class-block reader | `OpenElexSharp/Assets/ResourceClassBlockReader.cs` |
| Material cache and texture upload | `OpenElexSharp/Graphics/MaterialLibrary.cs` |
| CLI inspector | `OpenElexSharp/Assets/AssetCatalogConsole.cs` (`assets materials`) |

CLI:

```powershell
dotnet run --project OpenElexSharp -- assets materials AS_AlbSigns_1 --properties
dotnet run --project OpenElexSharp -- assets materials --all
```

## Related tasks

- T007 - DDS textures.
- T008b - mesh materials and texture bindings.
- T102 - full material/shader rendering.

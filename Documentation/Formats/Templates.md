# Templates (GTP0)

ELEX entity and item templates are stored as `tpl` resources in `w_tpl_*.db` databases. Each resource is a hashed `.rom` file containing a GTP0 wrapper around a Genome `class eCTemplateEntity` GEC2 object.

## References

The layout follows [rmtools](https://github.com/Baltram/rmtools) (GPL-3.0): `mCRisenDoc::DocumentRisen3Template` and `DocumentRisen3TemplateClass` in `mimicry/source/Mimicry/mi_risendoc.cpp`, plus the Elex Resource Manager template conversion.

## Resource layout

A template `.rom` resource contains nested wrappers:

```
GAR5                      outer resource header (44 bytes observed)
  uint32 0x20
  byte[36] zero/resource metadata
GAR5                      inner template header
  uint32 0x20
  char[4] "GTP0"
  uint64 timestamp
  GEC2 template object
```

The outer wrapper is 44 bytes for the observed retail templates. The parser locates the inner `GAR5 ... GTP0` header rather than relying on a fixed outer size.

## Template object

The GEC2 object must have class hash `0x60DE515C` (`class eCTemplateEntity`, version 6 in ELEX). Its class-data list contains at least:

- `class eCTemplateEntity` class data (54 bytes). The first 16 bytes are the **RefTemplateGUID** (parent template GUID). All-zero means no parent.
- `class eCEntity` class data, starting with:
  - 16-byte entity GUID
  - `uint16` name length
  - name bytes (Latin-1), usually ending in `.entity`
  - 6 bytes of unknown fixed data
  - `uint8` property-set count
  - that many GEC2 property-set objects
  - `uint32` child-template count
  - that many child GEC2 template objects

The child objects are full `eCTemplateEntity` objects and are parsed recursively. The retail data set uses composition heavily: a top-level item template usually has no property sets of its own, and one child template carries the mesh/property sets.

## Identity and resolution

- **GUID** — the eCEntity GUID.
- **Parent GUID** — first 16 bytes of the eCTemplateEntity class data.
- **Name** — resource name from the `RT02` database; the eCEntity name is retained separately (for example `1H_Axe_beak_Aura_fire.entity`).
- **Children** — embedded child templates, recursively parsed.
- **Property sets** — typed GEC2 objects (`eCMesh_PS`, `gCInventory_PS`, etc.) decoded with the T005b property decoder.

### Effective property sets

- `GetEffectivePropertySets()` resolves the parent GUID chain (root → self) and merges property sets by `(classHash, version)`, with later definitions overriding earlier ones.
- `GetAllPropertySets()` additionally walks child templates depth-first and merges their sets. This is the full entity/item composition and is the normal way to obtain item properties.

No nonzero parent GUIDs were found in the retail template set; variants and components are represented by child templates. Parent inheritance remains supported and is covered by synthetic tests.

## Example

`1H_Axe_beak_Aura_fire`:

```
top-level template (0 own property sets, 1 child)
  child 1H_Axe_beak_Aura_fire (1 property set)
    class eCMesh_PS v2
      MeshFileName = "PM_Weapon_Aura_1H_Axe_L1"
      MaterialSwitch = 15
      ObjectColor = (0, 0, 0)
      GIVoxelLevel = 2
      BlockVegetation = true
      ...
```

## Observed retail data

| Metric | Value |
| --- | ---: |
| Top-level template resources | 8,678 |
| Total templates including children | 17,356 |
| Templates with property sets anywhere in the composition | 8,673 |
| Maximum property sets in one composition | 15 |
| Parent links (nonzero RefTemplateGUID) | 0 |

The template database builds in about 4 seconds on the reference installation, including the resource-database scan.

## Implementation

| Component | File |
| --- | --- |
| GTP0 template parser | `OpenElexSharp/Assets/GenomeTemplate.cs` |
| Template database and lookup | `OpenElexSharp/Assets/TemplateDatabase.cs` |
| Resource discovery | `OpenElexSharp/Assets/ElexResourceIndex.cs` |
| Typed property sets | `OpenElexSharp/Assets/GenomePropertyDecoder.cs` |

CLI:

```powershell
dotnet run --project OpenElexSharp -- assets templates --limit 20
dotnet run --project OpenElexSharp -- assets templates 1H_Axe --limit 5 --properties
```

Output columns: name, GUID, resolved parent name, depth, own property-set count, inherited count, full-composition count, child count. `--properties` prints the full property sets with typed values.

## Open questions

- Meaning of the 6-byte unknown field after the eCEntity name.
- Whether RefTemplateGUID is used by mods or future data sets; retail templates have none.
- Full semantics of eCTemplateEntity fixed fields (position/rotation/unknown values).
- Distinguishing composition children from inheritance in UI/debug views.
- Whether language-specific template databases should be selected instead of mounting all.

## Related tasks

- T005/T005b — Genome object and property decoding.
- T006 — this template model.
- T010 — world entity placements referencing templates.

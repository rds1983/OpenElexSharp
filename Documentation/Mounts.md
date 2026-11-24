# Mounts and the virtual file system

How OpenElexSharp locates an ELEX installation and exposes its files under virtual paths.

## Installation layout

An installation is the game folder that contains `data/` and `system/`. The mount configuration is `data/ini/mount_packed.xml`.

The original game executable lives in `system/`, and its working directory is the base for relative volume roots. Therefore:

- **root** — the folder passed via `--elex-path` or `ELEX_INSTALL_PATH`;
- **app directory** — `<root>/system` when it exists, otherwise `<root>`;
- **mount file** — `<root>/data/ini/mount_packed.xml`.

Installation paths are machine-specific and must never be committed.

## `mount_packed.xml`

```xml
<MountList>
  <Volume Name="G3" RootPath=".." NoPacked="false">
    <Folders>
      <Folder VirtualPath="scripts" PhysicalPath="$(app)" />
      <Folder VirtualPath="ini" PhysicalPath="data/ini" />
      <Folder VirtualPath="data/extern" />
      <Folder VirtualPath="data/compiled" PackedPath="data/packed" />
    </Folders>
  </Volume>
</MountList>
```

| Element / attribute | Meaning |
| --- | --- |
| `Volume@Name` | Diagnostic name; may be empty. |
| `Volume@RootPath` | Base for the volume's relative folder paths, resolved against the app directory. Retail uses `..`, which resolves to the installation root. |
| `Volume@NoPacked` | Boolean (`true`/`false`/`1`/`0`, default `false`). Disables packed mounts for the volume; loose folders are unaffected. |
| `Folder@VirtualPath` | Required virtual folder to mount at. |
| `Folder@PhysicalPath` | Loose folder on disk. When omitted, the virtual path is used as a relative physical path under the volume root. |
| `Folder@PackedPath` | Folder containing PAK archives; its contents mount at `VirtualPath`. The PAK reader is implemented in T003, so only the mount is recorded for now. |
| `Folder@CreateFolder` | Read and stored. The engine never mutates the game installation, so it is currently ignored. |

Unknown elements and attributes are ignored for forward compatibility. DTDs are prohibited to avoid XML external-entity attacks.

### Macros

| Macro | Value |
| --- | --- |
| `$(app)` | The app directory (`<root>/system`, or `<root>` when `system` is absent). |
| `$(localappdata)` | The current user's local application data folder. |
| `$(savedgames)` | `<user profile>/Saved Games`. |

Macro names are case-insensitive. An unknown or unterminated macro is a configuration error.

## Virtual paths

- Forward slashes, no leading slash, no `.` or duplicate-slash segments.
- Lookups are case-insensitive; the original data mixes `Data/Compiled` and `data/compiled`.
- `..` and volume prefixes such as `#G3:/...` are rejected by the VFS API. Volume prefixes may appear in configuration files that resolve assets; mapping them to mounts is future work.
- Multiple sources may expose the same virtual path. Later mounts shadow earlier ones, which gives mods a natural overlay mechanism.

## Implementation status

| Component | File |
| --- | --- |
| Installation discovery and validation | `OpenElexSharp/Assets/ElexInstallation.cs` |
| Mount XML parser | `OpenElexSharp/Assets/MountConfiguration.cs` |
| Path/macro resolution | `OpenElexSharp/Assets/MountPathResolver.cs` |
| Loose folders | `OpenElexSharp/Assets/LooseFolderSource.cs` |
| PAK reader | `OpenElexSharp/Assets/PakArchive.cs` |
| PAK-backed source | `OpenElexSharp/Assets/PackedAssetSource.cs` |
| Layered VFS | `OpenElexSharp/Assets/AssetFileSystem.cs` |
| Packed mount records | `OpenElexSharp/Assets/PackedMount.cs` |

Packed folders are now readable: every `*.pak` in the folder is parsed and its entries are exposed under the folder's virtual root. Volumes are loaded in file-name order, so later volumes (mods) shadow earlier ones. Broken third-party volumes are skipped with a diagnostic; empty volumes simply add no entries. Unit tests use only synthetic temporary folders; the retail installation is exercised manually.

Archive internals are specified in [Formats/Pak.md](Formats/Pak.md).

## Evidence from the retail installation

- `data/ini/mount_packed.xml` maps `scripts` to `$(app)`, `ini` to `data/ini`, `data/extern` to itself, and `data/compiled` to the packed folder `data/packed`.
- `data/extern` contains loose textures and videos; with the app directory set to `system`, `RootPath=".."` resolves all relative paths correctly.
- Localization configuration (`data/ini/loc.ini`) references compiled strings with `#G3:/Data/Compiled/...`, confirming case-insensitive virtual paths.

## Open questions

- Whether `$(app)` means the executable folder or the game root. The engine assumes the executable folder because that makes `RootPath=".."` and all retail folder paths resolve consistently.
- Language selection among `c_*_en.pak`, `c_*_na.pak` and similar volumes. All volumes are currently mounted in file-name order.
- Override order when a loose folder and a PAK expose the same virtual path; currently mount order decides, with later sources winning.
- Exact semantics of `CreateFolder` for writable volumes (save games, screenshots); irrelevant for read-only assets.
- How `#Volume:/path` references in configuration files should map onto the VFS.

# Asset catalog and inspector

`AssetCatalog` is a searchable metadata snapshot of everything exposed by the virtual file system. The headless `assets` commands use it to inspect packed and loose files without launching the game window or writing throwaway C# code.

## Commands

```powershell
dotnet run --project OpenElexSharp -- assets help
dotnet run --project OpenElexSharp -- assets stats
dotnet run --project OpenElexSharp -- assets list [substring] [options]
dotnet run --project OpenElexSharp -- assets find <substring> [options]
dotnet run --project OpenElexSharp -- assets resources [substring] [options]
dotnet run --project OpenElexSharp -- assets objects <assetPath> [--limit <n>|--all] [--properties] [--elex-path <path>]
dotnet run --project OpenElexSharp -- assets templates [substring] [--limit <n>|--all] [--properties] [--elex-path <path>]
dotnet run --project OpenElexSharp -- assets export <assetPath> <destination> [--elex-path <path>]
```

All commands accept `--elex-path <path>`; otherwise `ELEX_INSTALL_PATH` is used.

### `list` / `find`

Prints one matching asset per line as tab-separated `path`, logical size in bytes, and source:

```
data/compiled/localization/en.str	1658980	G3:data/compiled/m_1_elex_overhaul_plus_v1_3.pak
```

| Option | Meaning |
| --- | --- |
| positional / `--filter <text>` | Case-insensitive substring of the virtual path. |
| `--ext <ext>` | Extension filter; `dds` and `.dds` are equivalent. |
| `--source <text>` | Case-insensitive substring of the source/mount name, for example `c_1_na`. |
| `--language <code>` | Language inferred from retail volume names (`c_1_en` → `en`). |
| `--limit <n>` | Maximum lines; default `100`. |
| `--all` | No limit. |
| `--include-binaries` | Include `.exe`, `.dll` and similar files in the listing. |

Executables are hidden from `list`/`find` by default. Machine-readable output goes to stdout; the match summary goes to stderr.

### `stats`

Prints total asset count, packed/loose split, executable count, total logical size, and tables grouped by extension and by source. Sizes are logical (decompressed) sizes, not bytes stored in the PAK.

### `resources`

Builds the logical resource index from `RT02` databases and prints resolved resources as tab-separated `name`, `size`, `type`, `language`, logical `.elex<type>` path, and ROM path. Supports `--type <token>` (for example `snd` or `dlg`), `--language`, a name substring, and the usual limit options.

```powershell
dotnet run --project OpenElexSharp -- assets resources --type snd --limit 20
```

### `objects`

Parses a `DT01` header document (for example `data/compiled/documents/w_quest.hdr`) and prints its top-level `GEC2` objects as tab-separated `name`, class hash, version, size, property count and class-data count. A single `GEC2` object and `GENOMFLE` string tables are also recognized. Use `--limit`/`--all` to control output.

```powershell
dotnet run --project OpenElexSharp -- assets objects data/compiled/documents/w_quest.hdr --limit 20
dotnet run --project OpenElexSharp -- assets objects data/compiled/documents/w_quest.hdr --limit 5 --properties
```

### `templates`

Builds the template database from `tpl` resources and prints name, GUID, resolved parent, inheritance depth, own/inherited/full-composition property-set counts and child count. `--properties` prints every property set in the full composition with typed values.

```powershell
dotnet run --project OpenElexSharp -- assets templates 1H_Axe --limit 5 --properties
```

### `export`

Copies one asset through the virtual file system, creating parent directories. The destination is overwritten. Executable formats are refused and missing assets return a non-zero exit code.

```powershell
dotnet run --project OpenElexSharp -- assets export data/compiled/localization/en.str out/en.str
```

## Catalog model

| Type | Purpose |
| --- | --- |
| `AssetCatalog` | Sorted entries, path lookup, statistics. |
| `AssetCatalogEntry` | Path, logical size, source, extension, language, packed flag, executable flag. |
| `AssetStatistics` / `AssetGroupStatistics` | Totals and grouped counts/sizes. |
| `AssetKinds` | Executable-extension classification. |
| `ElexResourceIndex` / `ElexResource` | Logical name → hashed ROM resolution through `RT02` databases. |
| `GenomeObject` / `GenomeHeaderDocument` / `GenomeStringTable` | GEC2 objects, DT01 documents and GENOMFLE string tables. |
| `GenomeTemplate` / `TemplateDatabase` | GTP0 templates, child composition and inheritance resolution. |

The catalog is metadata-only: building it does not open payloads. Export is the only command that reads asset bytes.

## Performance and caching

On the reference installation the catalog contains 156,954 entries (156,905 packed, 49 loose). A cold `assets stats` run, including mounting and parsing all PAK indexes, takes about 2.4 seconds. The catalog itself builds in well under a second after mounting.

`assets resources` additionally parses every `RT02` database; a cold run takes about 3.5 seconds and resolves 65,965 logical resources.

PAK indexes are parsed on every process start. A persistent index cache is deliberately not implemented yet: the current cost is acceptable and avoids cache invalidation bugs. It is tracked in the backlog.

## Language filter

Language is inferred only from retail-style volume names matching `c_<generation>_<code>.pak` or `m_<generation>_<code>.pak`, using the known game language codes. Loose files and differently named mods have no inferred language; use `--source` for those.

## Limitations

- All language volumes are mounted; `--language` filters the catalog rather than selecting a single volume.
- Path search is substring-based, not glob-based.
- Statistics use logical sizes, not archive storage size.
- Executable classification is extension-based.
- Resource database records are located via their `GEC2` class blocks; the reference metadata in the middle of a record is not decoded yet.
- Object properties and class data are preserved raw; type-specific decoding and nested `GEC2` objects are not implemented yet.

## Related tasks

- T002 — mounts.
- T003 — PAK reader.
- T004 — catalog and inspector.
- T004b — GAR5 wrappers, resource databases and logical resource names.
- T005 — Genome object model.

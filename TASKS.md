# OpenElexSharp Roadmap

Single source of truth for work. Agents follow the loop in [AGENTS.md](AGENTS.md).

Legend: `[ ]` open · `[~]` in progress (at most one) · `[x]` done · `[!]` blocked (reason noted inline)

## Working order

1. **Foundation** — toolchain, asset mounting, format research and loaders.
2. **Visible world** — render levels, objects, NPCs and creatures.
3. **Playable core** — movement, physics, interaction, combat, HUD.
4. **Game systems** — scripts, quests, dialogue, activities, progression, audio.
5. **Finishable game** — a full main-story playthrough without blockers.
6. **Parity** — custom shaders, post-processing and a UI skin that match the original.

Visual and UI parity are never a reason to delay playability. Stock MonoGame effects and plain Myra widgets are the default; custom shaders are allowed only when functionally required (for example, skinned meshes beyond `SkinnedEffect`'s bone limit) and must be documented as such.

Every format is documented in `Documentation/Formats/` **before** its loader is implemented. Research is based exclusively on game assets, never on game executables or libraries.

## Current focus

Milestone **M0 — Toolchain & Formats**. Next task: **T006**.

---

## M0 — Toolchain & Formats

Exit criteria: the solution builds and tests on a clean machine; the engine can mount an ELEX installation, enumerate and extract any asset, and parse the core object, mesh, texture, world, skeleton and template formats well enough to feed the renderer.

- [x] **T001 · Solution scaffold and CI**
  Create `OpenElexSharp.slnx`, `OpenElexSharp` (net10.0, MonoGame DesktopGL executable) and `OpenElexSharp.Tests` (xUnit, reference the engine). Add `Directory.Build.props` (nullable enabled, warnings as errors, deterministic builds), `.editorconfig`, `.gitignore`, and a GitHub Actions workflow that builds and tests.
  Done when: `dotnet build OpenElexSharp.slnx` and `dotnet test OpenElexSharp.slnx` pass with zero warnings; the app opens a MonoGame window and exits cleanly.
  Completed 2026-11-24 — Debug/Release build warning-free, 9 tests pass, local `--smoke-test` exits 0.

- [x] **T002 · Asset location and mount system**
  Resolve the ELEX installation from `ELEX_INSTALL_PATH` (and an equivalent launch argument), parse `data/ini/mount_packed.xml`, and expose a virtual file system that merges loose folders and packed volumes. Normalize virtual paths (case-insensitive), support enumeration and existence checks, and surface clear errors when the installation is missing.
  Done when: unit tests with synthetic mount files pass, and the engine can list and open loose files from a real installation without embedding absolute paths. See `Documentation/Mounts.md`.
  Completed 2026-11-24 — mount parsing, macro/path resolution and layered loose VFS; 99 tests; verified against the retail installation.

- [x] **T003 · PAK archive reader**
  Implement the G3V0 container format used by `data/packed/*.pak`: header, directory tree, per-entry metadata, compression and streamed reads. The GAR5 resource wrapper used by many compiled assets is asset-level and documented separately. Write `Documentation/Formats/Pak.md` first, covering layout, field semantics and open questions.
  Done when: every file in the shipped archives can be listed, and payloads can be extracted and re-read consistently; synthetic fixtures cover headers, compressed/uncompressed entries and corrupt data; `PackedMount` folders from T002 expose their contents through `AssetFileSystem`.
  Implemented per [Formats/Pak.md](Documentation/Formats/Pak.md) using rmtools/GameExtractor references. Validation on the test installation (including one mod) covers 156,905 packaged files, 111,661 of them compressed.
  Completed 2026-11-24 — G3V0 header/tree/attributes/raw-DEFLATE reader, PAK-backed VFS source, 127 tests and retail validation.

- [x] **T004 · Asset catalog and inspector**
  Build an index of all virtual assets (path, size, mount, extension) with fast lookup, filtering by mount and language, plus a developer tool (CLI or in-engine panel) to search paths, show statistics by extension, and export a single file for inspection. Cache the archive index if startup parsing is noticeable. This is the main research instrument for the tasks below. Note that some mounts expose the application folder, so binaries may appear in the index; report them separately and never open executable formats.
  Done when: listing the full archive index takes only a few seconds and the developer can inspect any asset without writing throwaway code.
  Implementation complete: `AssetCatalog` plus `assets list|find|stats|export`, `Documentation/Assets.md`, 167 tests; 156,954 entries indexed in ~2.4 s including mounting.
  Completed 2026-11-24 — catalog, headless inspector, binary safeguards, retail validation.

- [x] **T004b · GAR5 resource wrappers and resource databases**
  Parse the `GAR5` wrapper used by compiled assets (tag/version, `RT02`/`SNxx` sections), `E1RF` resource reference files, and the `w_*_*.db` hash-to-resource tables. The rmtools `ResolveRisen3Entries` implementation is the reference. Expose resolved resource names and sizes through the asset layer so hashed `.rom` payloads can be identified by logical name. Document in `Documentation/Formats/Gar5.md`.
  Done when: representative resource databases resolve names, a hashed dialogue/sound ROM can be addressed by its logical resource name, and synthetic fixtures cover wrappers, references and malformed indexes.
  Implementation complete: `Gar5Header`, `ResourceDatabase`, `Djb2Hash`, `E1RfFile`, `ElexResourceIndex`, `assets resources`; 203 tests; 65,965 resources resolved from retail data.
  Completed 2026-11-24 — GAR5/RT02/E1RF parsing, logical resource resolution and CLI inspection.

- [x] **T005 · Genome object serialization**
  Implement the GEL/Genome object model used by the `.r00`-family files: object headers, class identifiers, versioned property streams, references, arrays and unknown-property preservation. Use `ElexResourceIndex` when a logical resource must be located through an `RT02` database. Generate `Documentation/Formats/Objects.md`.
  Done when: representative objects of every serialized class in the game data load without unhandled errors; unknown classes/properties are skipped safely and reported.
  Implementation complete: `GenomeObject`, `GenomeHeaderDocument`, `GenomeStringTable`, `assets objects`; 228 tests; all 25 retail `.hdr` documents parse (749 `gCQuest`, 7,679 top-level `gCInfo` objects). Nested class-data objects and property type decoding remain raw.
  Completed 2026-11-24 — GEC2/DT01/GENOMFLE parsing and retail `.hdr` validation.

- [x] **T005b · Genome property decoding and nested objects**
  Decode common Genome property types (primitives, strings, GUIDs, vectors/matrices, arrays and proxies) and recursively enumerate nested `GEC2` objects inside class data. Build a practical class/property name table from hashes so object dumps are readable.
  Done when: representative `gCQuest`/`gCInfo` objects expose typed values, `gCInfo` commands/conditions are enumerable, and malformed values are rejected with clear diagnostics.
  Implementation complete: typed decoder for primitives/strings/GUIDs/vectors/enums/arrays/proxies, 18,855-entry name registry, gCInfo nested objects and `--properties`; 247 tests; all 25 retail `.hdr` documents parse and decode every property (48,493 nested gCInfo objects).
  Completed 2026-11-24 — property decoding, name registry and nested gCInfo objects.

- [ ] **T006 · Templates (`tple`)**
  Parse entity/item templates, their inheritance chains, default property values and cross-references into a queryable template database. Reuse `GenomeObject`, `GenomeHeaderDocument`, `GenomePropertyDecoder` and `GenomeNameTable` from T005/T005b; `GTP0` template documents are expected to use the same class model.
  Done when: any entity's full effective property set can be resolved by template name, and inheritance/override edge cases are covered by tests.

- [ ] **T007 · Textures**
  Parse texture assets (DDS with BC1–BC7 and mipmaps, plus any engine container) into `Texture2D`, with sRGB handling, fallback textures and a documented on-disk spec.
  Done when: diffuse/normal maps from the game display with correct orientation and alpha in a test scene.

- [ ] **T008 · Static meshes**
  Identify and parse static mesh assets: vertex layouts, index buffers, submeshes, materials, texture bindings and LODs. Document the vertex-stream combinations actually present in the data.
  Done when: representative meshes of every vertex layout load and render with stock effects; malformed data fails with actionable diagnostics.

- [ ] **T009 · Skeletons and animations**
  Parse bone hierarchies, bind poses, animation clips and compression. Record the maximum bone count in ELEX assets and whether stock `SkinnedEffect` suffices.
  Done when: skeletons load with correct bone naming/hierarchy, clips sample to plausible poses, and the bone-limit decision (stock vs. documented custom effect) is recorded.

- [ ] **T010 · World and level data**
  Parse the world/level assets that place entities and static geometry: sectors, transforms, object references, and the link between instances and templates/meshes. Document entity categories found in the data.
  Done when: a level's instance graph can be dumped as text (position, rotation, template, mesh) and matches what the game data implies.

- [ ] **T011 · Collision and navigation data**
  Extract collision geometry suitable for BepuPhysics2 static bodies, plus navigation mesh data for NPC pathing. Document both formats.
  Done when: a level's collision can be rendered as a wireframe/debug shape and matches visible geometry closely enough for movement.

- [ ] **T012 · Audio assets**
  Identify and document audio containers (FMOD banks/streams and any wrappers), including sample metadata and decoding requirements. Playback itself is scheduled in M3.
  Done when: every audio asset type in the data is identified and at least its metadata can be listed.

- [ ] **T013 · Text and localization**
  Parse string tables, `strings.csvt`, `loc.ini` and related localization data into a lookup API used by UI, dialogue and item names.
  Done when: item/UI strings for the selected language resolve from game data.

- [ ] **T014 · Script system research**
  Determine how game logic is stored and executed (bytecode/format, entry points, native API surface, quest/dialogue hooks) and write `Documentation/Formats/Scripts.md`. No runtime implementation in this task.
  Done when: the document explains how a simple script could be executed, with concrete evidence from assets and a list of unknown areas.

## M1 — Visible World

Exit criteria: a level, its static geometry and its characters can be explored with a debug camera using only stock MonoGame effects.

- [ ] **T101 · Application shell**
  Window, graphics device setup, resize handling, fixed timestep, content root, logging, crash screen, Myra host and a debug overlay (FPS, timings, memory).
  Done when: the app runs for minutes without leaks or device-lost crashes, and the overlay can be toggled.

- [ ] **T102 · Mesh renderer**
  Upload parsed meshes to GPU buffers, map materials/textures, and draw with `BasicEffect`; provide checker/placeholder materials for missing data. Add a model-viewer scene.
  Done when: static meshes render with correct proportions, normals and UVs, and the viewer can cycle any mesh in the catalog.

- [ ] **T103 · Camera, transforms and culling**
  Scene graph/transform hierarchy, fly camera, frustum culling, material-sorted draw batching, and basic render statistics.
  Done when: large test scenes render at interactive frame rates with culling visibly effective.

- [ ] **T104 · Level static geometry**
  Load a world sector, resolve its static meshes and transforms, and render the full sector with textures and fog.
  Done when: a recognizable game location is visible end to end, with no missing-geometry gaps caused by loader bugs.

- [ ] **T105 · Sky and lighting approximation**
  Skybox/background, sun directional light, ambient term and fog using stock effects; establish sane defaults per level.
  Done when: levels read clearly and are not flat-black or blown out.

- [ ] **T106 · Character rendering**
  Render NPCs and creatures via skeleton + `SkinnedEffect` (or the documented custom fallback), including multi-material bodies and equipment attachments.
  Done when: the bind pose of any humanoid/monster renders correctly in the character viewer.

- [ ] **T107 · Animation playback**
  Sample, loop and crossfade animation clips; drive idle/walk/run on the character viewer.
  Done when: clips play at correct speed without pops, and looping clips are seamless.

- [ ] **T108 · World visuals pass**
  Render light entities, decals, water surfaces and basic vegetation from level data; add debug toggles per category.
  Done when: the M1 level contains all major visual categories and each can be toggled independently.

## M2 — Playable Core

Exit criteria: the hero can traverse a level, collide with the world, talk to/interact with entities and fight, with a usable HUD.

- [ ] **T201 · Runtime world model**
  Map the level instance graph to runtime entities with stable IDs, ownership, lifetimes and component storage tailored for tens of thousands of instances.
  Done when: instance counts, categories and transforms match the T010 dump, and entity lookup by ID is O(1).

- [ ] **T202 · Spawn all entity types**
  Spawn hero, NPCs, monsters, items, containers, doors, interactive objects and triggers at their correct transforms with correct visuals.
  Done when: a level's population is visually complete and a category report shows no silently skipped entity type.

- [ ] **T203 · Player movement and camera**
  Capsule character on BepuPhysics2 with walk/run/crouch/jump, slope and step handling, and third-/first-person cameras with collision.
  Done when: the hero can traverse representative terrain, stairs and interiors without falling through or sticking.

- [ ] **T204 · Interaction**
  Focus raycast, interaction prompts, doors/levers/containers/pickups, and the inventory data model behind them.
  Done when: every interactive object category can be triggered and its state change is observable.

- [ ] **T205 · NPC navigation and idle behavior**
  Navmesh pathfinding, wander/idle schedules and simple obstacle avoidance; entities remain where the game data expects them.
  Done when: NPCs across a settlement move plausibly and do not fall off the navmesh.

- [ ] **T206 · Combat basics**
  Melee attacks with hit detection, stamina/health, damage, stagger, death and loot drops; ranged combat stubs where needed.
  Done when: the hero can defeat a basic enemy and be defeated, with correct animations and state transitions.

- [ ] **T207 · HUD and core menus**
  Myra HUD (health/stamina), inventory, pause/settings, key rebinding and a minimal journal.
  Done when: all core menus work with keyboard/mouse/gamepad and never block gameplay input irrecoverably.

- [ ] **T208 · Save/load foundation**
  Engine-owned save format for player state, inventory, entity states and world flags; versioned and validated.
  Done when: save/load round-trips across restarts and rejects incompatible versions cleanly.

## M3 — Game Systems

Exit criteria: scripts, quests, dialogue, activities, progression and audio work together; the main story is completable with temporary gaps explicitly tracked.

- [ ] **T301 · Script runtime**
  Execute game scripts on the VM/format documented in T014, with native bindings for world, inventory, quest and UI functions, plus graceful handling of unsupported calls.
  Done when: a representative set of shipped scripts runs and produces observable world changes.

- [ ] **T302 · Dialogue and quests**
  Dialogue trees, conditions, quest state machine, journal entries, objectives and script hooks.
  Done when: a complete side quest can be started, advanced and finished through dialogue and world actions.

- [ ] **T303 · Activities**
  Lockpicking/hacking, crafting, cooking, alchemy, trading, pickpocketing and workbenches.
  Done when: each activity can be performed from the world UI and affects inventory/progression correctly.

- [ ] **T304 · Progression and factions**
  Attributes, skills, learning/teachers, XP and levels, faction membership and reputation gates.
  Done when: a new character can be built in different directions and faction restrictions are enforced.

- [ ] **T305 · Streaming and transitions**
  Sector streaming, doors, teleporters, fast travel and respawn rules.
  Done when: the player can cross the whole world map without manual level loading or memory growth.

- [ ] **T306 · Audio runtime**
  Music, ambience, SFX and voiced dialogue with 3D positioning and mixing categories.
  Done when: each audio category plays in the right context and volumes are configurable.

- [ ] **T307 · Game flow**
  Main menu, intro/credits video playback (with a skip/fallback path), new game, load, endings and credits roll.
  Done when: a new game can be started from the menu and the ending sequence returns to the menu.

- [ ] **T308 · Finishable game**
  Drive the main story to completion, fix blockers and crashes, and record any intentional deviations.
  Done when: the main story can be played from start to credits without a blocking bug, and the test notes list the route taken.

## M4 — Parity

Exit criteria: visuals and UI are close enough to the original that differences are deliberate and documented.

- [ ] **T401 · Material shaders**
  Normal/specular/emissive materials, terrain layer blending and the remaining feature-specific effects, matching the original look.
  Done when: side-by-side comparisons show no systemic material differences.

- [ ] **T402 · Post-processing**
  Bloom, tone mapping, color grading, anti-aliasing and depth-of-field matching the original pipeline.
  Done when: the post stack is configurable and defaults reproduce the intended look.

- [ ] **T403 · Environment effects**
  Weather, time of day, water, wind/vegetation and particles.
  Done when: weather cycles and water behave like the original in representative locations.

- [ ] **T404 · UI parity**
  Myra skin, layout and transitions matching the original HUD and menus at all supported resolutions.
  Done when: screenshots at 16:9, 16:10 and ultrawide show equivalent layout and styling.

- [ ] **T405 · Animation parity**
  Blending, additive layers, look-at/IK, facial animation and root motion.
  Done when: conversations, combat and traversal animations match the original's feel.

- [ ] **T406 · Performance parity**
  LOD, occlusion, instancing, batching and streaming budgets.
  Done when: frame times at reference settings are within an agreed tolerance of the original on the same hardware.

- [ ] **T407 · Audio parity**
  Reverb zones, occlusion, distance curves and mix levels.
  Done when: indoor/outdoor transitions and combat mixes match the original.

---

## Backlog

Not committed to a milestone; revisit after T308.

- Mod loading/overrides on top of the base mounts.
- Persist PAK directory indexes to remove the ~2.4 s cold start at ~157k entries.
- Generalize nested class-data object decoding beyond `gCInfo`.
- Asset export/screenshot tools for documentation.
- Editor support for inspecting templates and worlds.
- ELEX 2 support if its formats are close enough.

## Known risks

- **Script runtime** is the largest unknown; it may require a large interpreter and extensive native bindings.
- **Animation compression** may be lossy in ways that complicate exact playback.
- **World streaming and occlusion** data may be only partially recoverable from assets.
- **Archive performance**: the retail archives are multi-gigabyte; random access and indexing must be efficient.
- **Save compatibility** with the original game is out of scope; OpenElexSharp defines its own save format.
- **Mount semantics**: the meaning of `$(app)` and of `#Volume:/path` references is inferred from the retail layout; revisit if assets fail to resolve.
- **Language-specific volumes**: `_en`, `_na` and other PAK variants are all mounted; a language preference is not implemented yet.

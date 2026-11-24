# AGENTS.md

Instructions for AI agents (and human contributors) working on OpenElexSharp. Read this file, [TASKS.md](TASKS.md) and [Documentation/README.md](Documentation/README.md) at the start of every session.

## Non-negotiable rules

1. **Never analyze game binaries.** Do not disassemble, decompile, dump, instrument or otherwise inspect `ELEX.exe`, shipped DLLs or any executable code. Only game **assets** may be examined, to determine their format. Document findings in `Documentation/Formats/`.
2. **Never commit game content.** No `.pak` files, no extracted assets, no absolute installation paths, no copyrighted screenshots or audio. Unit tests use small synthetic fixtures authored by us.
3. **Gameplay before parity.** Make entities visible and the game finishable first. Use stock MonoGame effects (`BasicEffect`, `SkinnedEffect`, …) and plain Myra widgets. A custom shader requires a documented functional reason (for example, more bones than `SkinnedEffect` supports); visual fidelity alone is not one.
4. **Formats are documented before implementation.** The spec in `Documentation/Formats/` is the source of truth for a loader. Update it when reality disagrees with it.
5. **Respect licenses.** Only use third-party code or format knowledge from sources whose licenses permit it. When an open-source project helps, record its link and contribution in the README "Credits" section.
6. **Never mark a task complete without explicit developer confirmation.** See the task loop below.
7. **Stay in scope.** One roadmap task per branch/commit. No drive-by refactors, no new dependencies without agreeing on them first.

## Task loop

Repeat until the developer stops giving you work.

### 1. Pick the next task

- Read `TASKS.md` and choose the **first unchecked task** in the current milestone (the one named under "Current focus"), unless the developer names a different task.
- Read the related docs and existing code before planning.
- Set the task to `[~]` (in progress) only in your working tree, and announce the task ID and a short plan. Keep at most one task in progress.
- If a task is underspecified or blocked, ask the developer before writing code.
- Machine-local information (such as the ELEX installation path) lives in `AGENTS.local.md`, which is git-ignored. Read it if present and never copy its contents into tracked files.

### 2. Do the work

- Follow the existing structure, naming and style. Keep parsing separate from rendering and gameplay.
- Add the format spec before or alongside the loader.
- Keep the change reviewable: prefer a small, coherent diff over a broad rewrite.

### 3. Test thoroughly

Testing is part of the task, not an afterthought.

- `dotnet build OpenElexSharp.slnx` with zero warnings.
- `dotnet test OpenElexSharp.slnx` — all tests pass, including new tests for new logic.
- Parser tests use synthetic fixtures covering valid data, version differences, truncated input and corrupt input. Do not make unit tests depend on the retail game.
- When a task touches real assets, run a local smoke test against the developer's installation and record what was verified (paths relative to the installation, counts, screenshots kept out of the repo).
- Rendering/UI tasks: run the app, exercise the feature interactively, and check the debug overlay/log for errors.
- Performance-sensitive work: measure before/after and note the numbers.
- If something cannot be verified automatically, say so explicitly in your report.

### 4. Update documentation

- Write or update the relevant `Documentation/` files (format specs, architecture notes, open questions).
- Update `TASKS.md` only with progress notes and discovered follow-up tasks; do **not** check the task off yet.
- Run `powershell -NoProfile -File Scripts/Get-CodeMetrics.ps1` (or `pwsh -NoProfile -File ...` where available) and update the metrics table in `README.md`.
- Keep `README.md` brief; details belong in `Documentation/`.

### 5. Ask the developer

Report back with:

- task ID and title;
- what changed (key files);
- how it was tested (commands and manual checks);
- known limitations, deviations and follow-ups;
- a request for explicit confirmation that the task is finished.

Do not infer confirmation from silence, a general "looks good" about something else, or your own test results. Wait for an explicit statement such as "task complete", "confirmed" or "mark it done".

### 6. Finish the task (only after confirmation)

1. Mark the task `[x]` in `TASKS.md` and add a one-line completion note (date, key outcome).
2. Review the whole remaining roadmap. You may reorganize it freely: add newly discovered tasks, split tasks that proved too large, remove tasks that are no longer needed, and update "Current focus" to the next task. Explain structural changes in the commit message.
3. Refresh README metrics.
4. Check `git status`/`git diff`; ensure no game assets, local paths or build output are staged.
5. Commit and push:
   - one commit for the task (include the roadmap reorganization and metrics in the same commit unless they are unrelated);
   - subject: `T0xx: <short imperative summary>`;
   - body: what changed, how it was tested, and any deviations or follow-ups;
   - `git push` to `origin` (never force-push).
6. Report the commit hash and the next task.

If the developer rejects the work or asks for changes, return to step 2 and repeat steps 3–5. The task stays `[~]` until confirmed.

## Testing standards

- xUnit for all automated tests; no test may require the ELEX installation, network access or a GPU.
- Prefer deterministic tests with explicit expected values over snapshot tests. Where golden files are unavoidable, keep them tiny and generated by our own writer.
- Cover boundaries: empty files, truncated headers, bad counts, duplicate paths, case differences, huge sizes.
- Tests must run in parallel and leave no temp files behind.
- Public APIs that parse untrusted data must fail with clear exceptions, never hang or allocate unbounded memory.

## Documentation standards

- Markdown only, in `Documentation/`.
- Format specs live in `Documentation/Formats/<Name>.md` and include: purpose, magic/version, endianness, layout tables, field semantics, compression/encoding, evidence, open questions, implementation status and related tests. See `Documentation/Formats/README.md`.
- Use relative links so the docs work on GitHub and locally.
- Do not paste large copyrighted data dumps; describe structure and cite small hex excerpts where necessary.
- Keep documents current: a stale spec is worse than no spec.

## Git conventions

- Never commit `bin/`, `obj/`, IDE state, `AGENTS.local.md`, extracted assets or logs.
- Do not use `git add -A` blindly; inspect the diff first.
- Do not rewrite published history, force-push or merge unrelated work into a task commit.
- Keep commits buildable and test-green.

## Code style

- C# uses tab indentation with a display width of 4; XML, JSON, YAML and Markdown use spaces.
- `.editorconfig` is the source of truth. After changing it, run `dotnet format whitespace OpenElexSharp.slnx` and keep generated files out of manual edits.

## Environment

- Windows is the primary development platform; keep code cross-platform (use `Path` APIs, avoid hard-coded separators).
- .NET 10 SDK is required (`dotnet --version` must be 10.x).
- Local game installation path: `AGENTS.local.md` → `ELEX_INSTALL_PATH`.
- Typical commands:

```powershell
dotnet build OpenElexSharp.slnx
dotnet test OpenElexSharp.slnx
dotnet run --project OpenElexSharp
powershell -NoProfile -File Scripts/Get-CodeMetrics.ps1
```

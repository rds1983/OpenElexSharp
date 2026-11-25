# Scripts, quests and dialogue logic

ELEX (Genome Engine) ships **no script bytecode or script source assets**. Game logic is split
between two layers:

1. **Data-driven programs** stored as Genome objects (GEC2) in DT01 documents and templates —
   most importantly the `gCInfo` command lists that drive dialogue and interactions, but also
   quest hooks, cutscene events, UI event bindings and combat behaviour trees.
2. **A native function registry** compiled into the game executable. The data references those
   functions by name through proxy types such as `eCScriptProxyScript`; the code itself is not
   present in any inspected asset.

The game therefore has an *object-threaded* logic format rather than a conventional VM: the
"opcodes" are GEC2 class hashes and the "operands" are typed object properties. Reimplementing
game logic means writing an interpreter for the data side plus C# bindings for the native
functions (T301/T302). No executable or DLL was inspected for this document.

## Purpose and where logic lives

| Asset | Content | Logic role |
| --- | --- | --- |
| `data/compiled/documents/w_info.hdr` | 7,679 `gCInfo` entries, 40,900 commands and 7,593 conditions | Dialogue and interaction programs |
| `data/compiled/documents/w_quest.hdr` | 749 `gCQuest` entries | Quest definitions with 7 named script hooks each |
| `data/compiled/documents/w_cutscene.hdr` | 34 `gCCutscene` entries, 38 top-level objects, 13,983 GEC2 objects | Cutscene timelines and script events |
| `data/compiled/documents/w_ui.hdr` | 8 `gCUIState` entries, 801 GEC2 objects | UI state/model event bindings |
| `data/compiled/documents/w_combatbehavior.hdr` | 252 `gCCombatBehaviorDoc` entries | Serialized combat behaviour trees |
| `data/compiled/0_na_tpl/*` | 8,678 templates | Entity/UI/AI hook properties (106,249 proxy slots) |
| Other `w_*.hdr` documents | camera, combat, movement, perception, tutorial, … | Additional named event hooks |
| `data/compiled/localization/en.str` | STB string table | Dialogue and quest text addressed by ID hash |

No `RT02` resource type stores code. The 26 resource types exposed by `assets resources` are
`bln`, `clo`, `col`, `cut`, `dlg`, `eff`, `fll`, `flm`, `fnt`, `grp`, `img`, `mat`, `msh`,
`net`, `ocm`, `prj`, `pxc`, `sec`, `shd`, `skn`, `snd`, `spt`, `tfn`, `tpl`, `vid`, `wrl`.
There is no `.script`, `.dat`, `.lua`, `.nut`, `.asm` or bytecode asset. The `0_na_nam` pair is a
navigation database (`Nav_Testlevel`, `Nav_World`) that resolves no resources of its own.
`scripts/` contains only native redistributables (`binkw64`, FMOD, PhysX, …) and the game
executable, which is out of scope.

## Container and encoding

All structures here are Genome objects; see [Objects.md](Objects.md) for GEC2, DT01 and the
hash conventions. All integers are little-endian. Relevant payload encodings observed in
documents:

| Type | Payload |
| --- | --- |
| `bool` | 1 byte (`0`/`1`). |
| `int`, `unsigned int` | 4 bytes. |
| `enum` | `uint32` member-name hash, then `uint32` integer value. |
| `class bCString` | `uint32` length, then Latin-1 bytes (no terminator). |
| `eCScriptProxyScript`, `gCScriptProxyAIFunction`, `gCScriptProxyAIState`, UI proxies | `uint16` version (observed `1`), `uint16` length, then Latin-1 function name. |
| `gCInfoLocString`, `gCQuestLocString`, other `*LocString` | `uint32` ID hash, `uint16` length, Latin-1 string ID. |

The four bytes that [Objects.md](Objects.md) describes as a skipped `*LocString` prefix are the
DJB2 hash of the ID. For example the `LogTopic` of `Player_CollectElexBattery` begins
`9c f9 60 8d 27 00 51 55 45 53 54 5f …` = hash `0x8D60F99C`, length 39, `QUEST_GARTMANNI3_2015-02-03_12-55-25_00`
(39 characters). This is the link to [Localization.md](Localization.md): the string-table key is
the case-insensitive DJB2 hash of the **plain ID**, not of a `loc.ini` prefix. All 27,089 of the
27,090 distinct dialogue/quest IDs it was checked against resolve in `en.str`; the single
miss (`INFO_WEIDMANNI5_00000001`) is absent from the mounted tables (mod/retail mismatch).

The `bCString` length-prefixed encoding above is what the existing object inspector already
prints; the proxy and loc-string layouts are the additional details needed for a script layer.

## Dialogue and interaction programs (`gCInfo`)

A `gCInfo` is one dialogue or interaction entry. Top-level properties (all 7,679 objects have
the same set):

| Property | Meaning (inferred from name and usage) |
| --- | --- |
| `Name` | Info ID, also the key other infos reference (`PlayerKnows`) |
| `Owner`, `Partner` | Speaking and listening entity names (`player`, `Konrad_Drone`, …) |
| `Type`, `ConditionType` | Info category (`gEInfoType_*`, `gEInfoCondType_*`) used when selecting entries |
| `Quest` | `gCQuestProxy` this info belongs to |
| `MainSortID`, `SortID` | Selection order |
| `StartChapter`, `EndChapter` | Chapter gate (`-1` = none) |
| `InfoGiven`, `LastExecutionTimestamp` | Runtime state; shipped documents store `false` / `-1` |
| `GoldCost`, `GoldCostScript`, `GoldCostItem` | Price or a script that computes it |
| `RequiredPerks` | Perk gate |
| `Smalltalk*` | Ambient smalltalk range/timing/exit routines |
| `CurrentInfoCommandIndex`, `InfoIsRunnig` | Runtime cursor and running flag |

The executable part is in the object's class data:

```text
uint16 commandCount
uint16 conditionCount
GEC2[commandCount]      command objects
GEC2[conditionCount]    condition objects   // gating conditions for the whole info
```

`w_info.hdr` contains 48,493 nested command/condition objects, matching the existing inspector's
count. The `gCInfo` class version is 1.

### Commands

There are 39 command classes and 40,900 instances:

| Command | Count | Purpose |
| --- | ---: | --- |
| `gCInfoCommandSay` | 25,414 | Speak a localized line |
| `gCInfoCommandSetGameEvent` | 3,712 | Set a named game variable/event |
| `gCInfoCommandRunScript` | 2,395 | Call a named native function |
| `gCInfoCommandSwitchCase` | 1,030 | Conditional branch (own condition list) |
| `gCInfoCommandEnd` | 1,012 | End the program |
| `gCInfoCommandSwitchEnd` | 903 | Close a branch |
| `gCInfoCommandDescription` | 643 | Description text |
| `gCInfoCommandGive` | 565 | Move items between entities |
| `gCInfoCommandStartEffect` | 527 | Start a named effect |
| `gCInfoCommandInfoWait` | 501 | Pause/yield |
| `gCInfoCommandStartFixCamera` | 490 | Cutscene camera |
| `gCInfoCommandSucceedQuest` | 447 | Quest state change |
| `gCInfoCommandSetRoutine` | 384 | Assign an NPC routine |
| `gCInfoCommandClearGameEvent` | 299 | Clear a game event |
| `gCInfoCommandRunQuest` | 282 | Quest state change |
| `gCInfoCommandPlayJingle` | 250 | Music cue |
| `gCInfoCommandCloseQuest` | 237 | Quest state change |
| `gCInfoCommandStopFixCamera` | 232 | Cutscene camera |
| `gCInfoCommandRemoveQuestActor` | 187 | Quest bookkeeping |
| `gCInfoCommandSetSectorStatus` | 171 | World state |
| `gCInfoCommandAddLogText` | 142 | Journal entry |
| `gCInfoCommandAttack` | 136 | Make an NPC attack |
| `gCInfoCommandSaySVM` | 131 | Smalltalk/voice-line selection |
| `gCInfoCommandSwitchDefault` | 124 | Default branch |
| `gCInfoCommandGiveXP` | 104 | Experience |
| `gCInfoCommandAddQuestActor` | 102 | Quest bookkeeping |
| `gCInfoCommandRunAIScript` | 66 | Call a named AI function |
| `gCInfoCommandBack` | 62 | Rewind/return |
| `gCInfoCommandSetTradeEnabled` | 53 | Dialogue state |
| `gCInfoCommandAddInfoSystemEndScript` | 50 | Deferred script call |
| `gCInfoCommandSleep` | 40 | NPC action |
| `gCInfoCommandSetPartyEnabled` | 37 | Party state |
| `gCInfoCommandDestroyItem` | 36 | Inventory |
| `gCInfoCommandKillNPC` | 32 | Kill an entity |
| `gCInfoCommandSetTeachEnabled` | 28 | Dialogue state |
| `gCInfoCommandCreateItem` | 28 | Inventory |
| `gCInfoCommandAddCrewMember` | 25 | Crew state |
| `gCInfoCommandRemoveCrewMember` | 19 | Crew state |
| `gCInfoCommandSayConfirm` | 4 | Confirmation line |

`gCInfoCommandRunScript` carries the native call target and arguments:

```text
Script      eCScriptProxyScript   // function name, e.g. OnQuestChange_Endgame
Self        bCString             // entity name
Other       bCString             // entity name
OtherType   enum gEOtherType     // how Other is resolved
Param       int
StringParam bCString
```

`gCInfoConditionScript` uses the same argument model with a plain `ScriptName` string, and
`gCInfoCommandRunAIScript` uses `AIScript` (an `gCScriptProxyAIFunction`).

### Conditions

There are 15 condition classes and 7,593 top-level instances (1,043 infos have two, 312 have
three, 49 have four and 24 have five or more; 2,015 infos have none):

| Condition | Count |
| --- | ---: |
| `gCInfoConditionPlayerKnows` | 2,675 |
| `gCInfoConditionPlayerNotKnows` | 2,367 |
| `gCInfoConditionQuestStatus` | 1,143 |
| `gCInfoConditionNPCStatus` | 441 |
| `gCInfoConditionOwnerInArea` | 312 |
| `gCInfoConditionScript` | 171 |
| `gCInfoConditionLastExecutionTimestamp` | 147 |
| `gCInfoConditionNPCCrewMember` | 95 |
| `gCInfoConditionItemAmount` | 91 |
| `gCInfoConditionGameVariable` | 73 |
| `gCInfoConditionNPCAreaStatus` | 25 |
| `gCInfoConditionTeachEnabled` | 24 |
| `gCInfoConditionNPCRoutineStatus` | 16 |
| `gCInfoConditionOwnerNearEntity` | 10 |
| `gCInfoConditionSkillValue` | 3 |

`gCInfoConditionScript` is the bridge into native predicates (`ScriptName`, `Self`, `Other`,
`OtherType`, `Param`, `StringParam`). Its typical values are `IsAvailable`, `IsCrewMember`,
`HasCommittedCrime`, `IsSoul_InSyntheticRange`.

### Branching

`gCInfoCommandSwitchCase` is a container. Its class data is:

```text
uint16 conditionCount
GEC2[conditionCount]   condition objects belonging to this case
```

then the following commands up to `gCInfoCommandSwitchEnd` form the branch body;
`gCInfoCommandSwitchDefault` introduces the default branch. Most cases carry one to three
conditions (783 cases with 1, 221 with 2, 20 with 3; a few have none or up to 6). Whether all
conditions must hold (AND) is strongly implied by the "case list" shape but not proven from
assets alone.

Concrete example, `IKENI7_02560` (4 commands, 1 gating condition):

```text
condition: PlayerKnows("Chloe_LastFavour")
command 0: Say(player, INFO_IKENI7_00009336)
command 1: SwitchCase
             count = 3
             QuestStatus(player_collectchloebag01, current)
             QuestStatus(player_collectchloebag02, current)
             QuestStatus(player_collectchloebag03, current)
command 2:   SetGameEvent("Chloe_FixMoneyCollector")
command 3: SwitchEnd
```

### A complete worked example

`GARTMANNI3_00111` has 5 commands and 1 condition:

```text
condition 0: PlayerNotKnows("Arx_Choi")

command 0: Say(player, INFO_GARTMANNI3_0000...)
command 1: Say(Arx,    INFO_GARTMANNI3_0000...)
command 2: SetGameEvent("Arx_Choi", 0)
command 3: Say(player, INFO_GARTMANNI3_0000...)
command 4: Say(Arx,    INFO_GARTMANNI3_0000...)
```

Read as a script:

```text
if not PlayerKnows("Arx_Choi"):
    Say(player, "…")      # text resolved through the STB table
    Say(Arx,    "…")
    SetGameEvent("Arx_Choi", 0)
    Say(player, "…")
    Say(Arx,    "…")
```

The `Say` text IDs are stored as `gCInfoLocString`; resolving one is the T013 path:
`INFO_PANKRATZI7_00005699` → DJB2 `0x10115597` → `English_Text` row
“Just think of the things I can achieve if this allows me to live forever.”

### Execution model (hypothesis for T301)

The data supports this runtime, but the exact engine behaviour must be validated when the
runtime is written:

1. **Selection.** On interaction, enumerate `gCInfo`s whose `Owner`/`Partner` match the
   speakers, whose `ConditionType` matches the context (general dialogue, trade, training, …),
   whose chapter range allows the current chapter, whose gating conditions pass, and which are
   not already `InfoGiven` (unless `Permanent`). Sort by `MainSortID`/`SortID`.
2. **Frame.** Create an execution frame with the chosen info, `Self`/`Other` speaker pair and a
   program counter at command 0; `InfoIsRunnig = true`.
3. **Step.**
   - `Say`/`SaySVM`/`SayConfirm`: queue the localized line and voice for the speaker, then wait
     for it to finish (or advance immediately if silent).
   - `SwitchCase`: evaluate its conditions; if true, enter the branch (execute its commands
     until `SwitchEnd`), otherwise skip to the next case/default.
   - `SwitchDefault`: enter the default branch if no case matched; otherwise skip.
   - `SwitchEnd`: leave the branch.
   - `End`: stop the program.
   - `Back`: return to the previous selection/parent info (semantics not proven).
   - `InfoWait`: yield for a duration (the single observed operand is `0x2328` = 9000; the
     unit is not confirmed, `Time` in milliseconds is a guess).
   - State commands (`SetGameEvent`, `SucceedQuest`, `Give`, `SetRoutine`, …): mutate world,
     inventory, quest and NPC state.
   - `RunScript`/`RunAIScript`: resolve the name through the native registry and call it with
     `(Self, Other, OtherType, Param, StringParam)`.
4. **Finish.** Apply quest side effects, mark `InfoGiven`, store `LastExecutionTimestamp`,
   `InfoIsRunnig = false`, and return to the parent selection.

`gCInfo` execution is re-entrant in the sense that a native function can queue another info or
state change; no explicit call stack is stored in the document.

## Quest logic

`gCQuest` (class version 6) has 749 entries and 33 properties. Important fields:

| Property | Meaning |
| --- | --- |
| `Type` | `gEQuestType_*` (kill, follow NPC, has items, …) |
| `LogTopic`, `LogDesc` | Localized journal strings (`gC*LocString`) |
| `Parent`, `FinishedQuests` | Quest tree |
| `Status` | Runtime `gEQuestStatus` |
| `RunningQuestScript`, `SucceededQuestScript`, `FailedQuestScript`, `ObsoleteQuestScript`, `CancelledQuestScript`, `LostQuestScript`, `WonQuestScript` | Named native hooks |
| `ActivationTime_Year/Day/Hour`, `StartChapter`, `EndChapter` | Scheduling |
| `Actors`, `DeliveryEntities`, `DestinationEntity`, `GuideWalkMode` | Objectives |
| `ExperiencePoints`, `ModifySkillsSuccess` | Rewards |

All seven hook slots exist on every quest (5,243 slots). 344 are non-empty: 174 succeeded,
102 running, 17 each failed/obsolete/cancelled/lost, and none of the 749 quests define a
`WonQuestScript`. Hook names are native functions such as `OnSuccess_QuestNPC_IsDead`,
`OnQuestChange_Endgame`, `OnQuestChange_RagnarRestoreLaw`.

Infos manipulate quests through the command layer (`RunQuest`, `SucceedQuest`, `CloseQuest`,
`AddLogText`, `AddQuestActor`, `RemoveQuestActor`, `GiveXP`), so quests are data objects whose
state transitions are driven by dialogue programs and native events, not by a separate quest
script language.

## Cutscenes

`w_cutscene.hdr` contains 34 `gCCutscene` objects plus 4 sequence-channel setup arrays. Each
cutscene has `StartScript` and `EndScript` proxies; the retail values are `OnCutsceneStart` and
`OnCutsceneEnd`. Actor setups add per-actor `OnCutsceneActorStart`/`OnCutsceneActorEnd` pairs
(524 each across the document). Cutscene events include `gCCutsceneEventRunScript` (72
instances), which calls a named native function at a timeline position.

The cutscene timeline itself is separate from the script layer and is not covered here.

## Entity, AI, item and UI hooks

Templates attach native functions through property sets. The 8,678 retail templates contain
106,249 proxy property slots across 65 distinct hook names; 36,268 are non-empty and reference
264 distinct functions. The most common hooks:

| Property set | Hook | Template instances |
| --- | --- | ---: |
| `gCInteraction_PS` | `EnterROIScript` / `ExitROIScript` | 4,182 / 4,180 |
| `gCInteraction_PS` | `GeneratePlunderScript` | 4,168 |
| `gCInteraction_PS` | `FocusNameScript` / `FocusPriorityScript` | 4,142 / 4,069 |
| `gCItem_PS` | `CanEquipScript` / `OnEquipScript` / `OnUnEquipScript` | 2,563 each |
| `gCScriptRoutine_PS` | `Routine` (AI state entry) | 1,332 |
| `gCScriptRoutine_PS` | `ResumeTask`, `CurrentTask`, `LastTask`, `CurrentState` | 1,332 each |
| `gCPerception_PS` | `AttitudeScript` / `OnTickScript` | 1,329 / 1,207 |
| `gCStateGraph_PS` | `OnEnterState` / `OnExitState` | 184 each |
| `gCInventory_PS` | `OnConsumeItem` | 107 |
| `gCInteraction_PS` | `DamageScript`, `TouchScript`, `TriggerScript`, … | ~4,185 each |

AI routines are data too: `gCScriptRoutine_PS.Routine` names the native AI state that runs the
routine (12 distinct values, e.g. `ContinueRoutine` 1,100, `Rtn_LieDead` 88,
`ContinueRoutine_CombatHelper` 14). `CurrentTask`/`LastTask`/`CurrentState` are
`gCScriptProxyAIState` slots that are **empty in every template**; they are runtime state, not
authoring entry points. `ResumeTask` (`ResumeTask` 1,331) and `OnTickScript`
(`CheckForImportantInfo` 1,207) are the other AI hooks.

UI logic is also object data. `w_ui.hdr` embeds `gCUIEventScriptProxy`,
`gCUICanExecuteScriptProxy`, `gCUIExecuteScriptProxy`, `gCUICancelEventScriptProxy` and
`gCUIMessageScriptProxy` objects inside UI state data; 112 distinct handler names are used, for
example `UI_InventoryPageModel_OnInitialize`, `UI_InventoryPageModel_OnProcess`,
`UI_InventoryPageModel_Player_OnActivate`, `UI_EndGame_Credits_OnFinished`.
`w_input.hdr` binds input actions (not logic).

## Combat behaviour trees

`w_combatbehavior.hdr` contains 252 `gCCombatBehaviorDoc` objects (class data version 5). Each
document stores a named behaviour (`BehaviorName`, e.g. `Complex_MoveCloserToTargetRanged`) and
one opaque payload (986 bytes for that example) containing behaviour names, human-readable node
labels and native AI function names:

```text
Complex_MoveCloserToTargetRanged
  Run closer to target / Run to target
  Monitor not in aim mode / Condition_InAimMode
  Monitor too far / Condition_TooFar
  Movement_RunCloserToTarget
  Condition_FreeLOS / Condition_CanGoToTarget
  Movement_GoCloserToTarget / Movement_GoToCloseRandomPos
  Condition_EvadeBack / Movement_GoAwayFromTarget
  Condition_GotoPosition
```

The tree layout is not decoded; only the embedded names and the outer GEC2 wrapper are
confirmed. The same data-driven pattern applies as elsewhere: conditions and actions are names
resolved against the native registry.

## The native function registry

All behaviour ultimately resolves to names. The proxy payloads are versioned strings, and the
same name space is shared across documents and templates. Across the inspected assets:

| Source | Distinct names | Non-empty uses |
| --- | ---: | ---: |
| Documents (`scripts` proxies) | 689 | 4,728 |
| Documents (`ui_*` proxies) | 112 | 361 |
| Documents (`AI function` proxies) | 3 | 66 |
| Templates (`eCScriptProxyScript`, `AI*`) | 264 | 36,268 |
| **Union** | **1,064** | — |

Names fall into recognisable families: engine events (`OnEnterProcessingRange`,
`OnExitProcessingRange`, `OnCutsceneStart`, `OnQuestChange_*`, `OnSuccess_*`,
`OnPrefetch_*`), predicates (`IsAvailable`, `IsCrewMember`, `CanHear`, `CheckTalk`,
`Condition_Holds1HSword`), queries (`GetAttitude`, `GetFocusEntityName`,
`GetFocusEntityPriority`, `GetCrimeCosts`), state entry points (`ContinueRoutine`, `Rtn_*`),
UI handlers (`UI_*`, `TE_*`), and effect/combat helpers (`Movement_*`, `Condition_*`).

The registry is not stored in assets: the names are string operands, and the functions live in
the executable. The class `gCScriptAdmin` (hash `0xFE49A9C3`) exists in the name registry but has
no instance in any document or template, so it is presumably a runtime singleton. This is why
T301 cannot "load the scripts" — it must provide a binding table keyed by these names and fail
gracefully (log once, return a default) for unknown ones, exactly as a mod that adds a template
referencing a function the current build lacks would.

## Evidence

| Measurement | Value |
| --- | ---: |
| Catalog assets / resources | 156,954 / 65,965 |
| DT01 documents inspected | 25 |
| GEC2 objects in those documents (nested included) | 124,230 |
| `gCInfo` entries | 7,679 |
| Info commands / conditions | 40,900 / 7,593 |
| Distinct command / condition classes | 39 / 15 |
| Distinct dialogue/quest loc IDs | 27,090 |
| Loc IDs resolving in `en.str` | 27,089 |
| `gCQuest` entries | 749 |
| Quest hook slots / non-empty | 5,243 / 344 |
| `gCCutscene` entries | 34 |
| Templates | 8,678 |
| Template proxy slots / non-empty | 106,249 / 36,268 |
| Distinct native function names (union) | 1,064 |

Counts are taken from the mounted reference installation, which includes the `ELEX Overhaul`
mod volume (`m_1_elex_overhaul_plus_v1_3.pak` supplies `en.str`) alongside the retail `c_1_na`
and `c_1_en` volumes. Retail-only counts can therefore differ slightly; the structural findings
(GEC2 class data, proxy types, hook properties) are defined by the format, not by the volume
that supplied a given object.

Small excerpts (all relative paths):

```text
w_info.hdr, gCInfoCommandSwitchCase class data:
  02 00                    conditionCount = 2
  47 45 43 32 ...          first condition GEC2 (gCInfoConditionPlayerKnows)
  47 45 43 32 ...          second condition GEC2

w_quest.hdr, Player_CollectElexBattery.LogTopic:
  9c f9 60 8d              uint32 DJB2("QUEST_GARTMANNI3_2015-02-03_12-55-25_00")
  27 00                    uint16 length = 39
  51 55 45 53 54 5f ...    "QUEST_GARTMANNI3_2015-02-03_12-55-25_00"
```

## Open questions

- Native function signatures, return types and name resolution (case sensitivity, overloads,
  whether names are resolved at load or first call).
- Exact `SwitchCase` semantics: the condition list strongly implies AND, but OR, first-true and
  fall-through behaviour are not proven.
- `gCInfoCommandBack` target and `InfoWait` units (the only observed value is 9000; ms is a
  guess).
- Whether `Say` always blocks until the line/voice finishes and how `SaySVM` selects the voice.
- Complete meaning of `gEInfoCondType`, `gEInfoType`, `gEOtherType` and their runtime use.
- The binary layout of `gCCombatBehaviorDoc` class data (behaviour tree, node types, links).
- The `gCScriptAdmin` object: no asset instance exists; its runtime role is unknown.
- Whether the retail executable implements every name found in the mounted mod's templates
  (one loc ID is already missing, which suggests version skew).
- Save-game representation of `InfoGiven`, quest status and AI routine task state (out of scope
  for T014).
- Whether any non-PC build ships script code in a different container (not observable here).

## Implementation status

Documentation only; T014 deliberately adds no loader or runtime code. The research instruments
were throwaway scripts against exported assets, not committed.

Follow-ups:

- Extend the object inspector with a decoded `gCInfo` command/condition view (a natural T301
  prerequisite, for example `assets infos <id>`).
- T301: implement the info interpreter, switch/condition evaluation and the native binding
  table with graceful fallbacks.
- T302: connect `gCInfo` selection to dialogue UI, quest state, journal and voice playback.
- When the info layout is implemented, move the `gCInfo` class-data layout into `Objects.md`
  or cross-reference it from there, and add synthetic parser tests (valid, truncated, bad
  counts).

## Related documents

- [Objects.md](Objects.md) — GEC2 objects, DT01 documents, name hashes.
- [Templates.md](Templates.md) — template composition and property sets.
- [Localization.md](Localization.md) — STB string tables and ID hashing.
- [Worlds.md](Worlds.md) — scene sectors and entity placement.

## References

- [ELEX scripting](https://www.nexusmods.com/elex/mods/64) — community documentation confirming
  that ELEX logic is modified through `gCInfoCommand*`/infoscript data and `.hdr` documents,
  consistent with the absence of a bytecode asset.
- [rmtools](https://github.com/Baltram/rmtools) (GPL-3.0) — class/property name registry used to
  resolve GEC2 hashes (already credited in [Objects.md](Objects.md)).
- No game executable or DLL was analysed.

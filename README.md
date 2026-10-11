# Life of Empires

An Age-of-Empires-style real-time strategy game for iPhone, built in Unity 6.6 with URP.
This repository currently contains **Milestones 1–3**: the RTS battle — economy, workers,
pathfinding, construction, combat, selection, an isometric camera and a state-machine enemy AI —
plus an in-battle HUD (resource bar, selection panel, build menu, production panel, results card),
the ranged archer, and campaign unlock progression, with two playable missions' worth of data.

No commits, pushes or deployments are made by the build tooling here.

## 1. Requirements

| Requirement | Value |
|---|---|
| Unity | `6000.6.5f1` (Unity 6.6), arm64 macOS install at `/Applications/Unity/Hub/Editor/6000.6.5f1-arm64` |
| Packages | URP 17.6.0, Input System 1.20.1, uGUI 2.6.0, Test Framework 1.8.0 (pinned in `Packages/manifest.json` to the versions bundled with this editor) |
| Xcode | 26.3 / iOS SDK 26.2 (**installed**) |
| iOS Build Support module | **NOT installed** — required before an iOS player build; see `IOS_RELEASE.md` §2 |
| Active input handling | Both (`ProjectSettings.asset: activeInputHandler: 2`); runtime input code compiles under `ENABLE_INPUT_SYSTEM` **and** `ENABLE_LEGACY_INPUT_MANAGER` |

## 2. Quick start

```bash
# 1. Regenerate every generated asset: URP assets, quality tier, iOS player settings,
#    ScriptableObject content, materials, and both scenes. Idempotent.
U=/Applications/Unity/Hub/Editor/6000.6.5f1-arm64/Unity.app/Contents/MacOS/Unity
gtimeout 600 "$U" -projectPath "$PWD" -batchmode -quit -nographics \
  -executeMethod LifeOfEmpires.Editor.ProjectBootstrap.Run -logFile /tmp/loe_bootstrap.log
# expect: URP_PROBE_OK ... and BOOTSTRAP_OK in the log
```

In the editor: `LifeOfEmpires > Bootstrap Project` runs the same thing, then open
`Assets/LifeOfEmpires/Scenes/Battle.unity` and press Play. `Bootstrap.unity` is the shipped entry
point: it loads `Battle` on start. Both scenes are in `EditorBuildSettings` with `Bootstrap` first.

```bash
# 2. Run the suites
gtimeout 900 "$U" -projectPath "$PWD" -batchmode -nographics \
  -runTests -testPlatform EditMode -testResults /tmp/loe_editmode.xml -logFile /tmp/loe_v3.log
gtimeout 900 "$U" -projectPath "$PWD" -batchmode -nographics \
  -runTests -testPlatform PlayMode -testResults /tmp/loe_playmode.xml -logFile /tmp/loe_v4.log
```

Verified results for this build: **EditMode 68/68 passed**, **PlayMode 2/2 passed**, batch-mode
compile with **0 `error CS`**, and the bootstrap logs `URP_PROBE_OK default=LifeOfEmpires_URP
current=LifeOfEmpires_URP type=UniversalRenderPipelineAsset` followed by `BOOTSTRAP_OK`.

## 3. Layout

```
Assets/LifeOfEmpires/
  Scenes/      Bootstrap.unity (entry point)  Battle.unity (the battle)
  Scripts/Runtime/   game code (LifeOfEmpires.Runtime asmdef), incl. UI/ (battle HUD)
  Scripts/Runtime/Campaign/  CampaignManager (unlock rules, next playable mission)
  Scripts/Editor/    ProjectBootstrap.cs (LifeOfEmpires.Editor asmdef)
  Data/              ScriptableObjects: units, buildings, missions, difficulty, economy
  Art/Materials/     URP/Lit materials generated from the palette
  Tests/EditMode/    unit + system tests (LifeOfEmpires.Tests.EditMode asmdef)
  Tests/PlayMode/    end-to-end battle flow tests (LifeOfEmpires.Tests.PlayMode asmdef)
  Settings/          generated URP renderer + pipeline assets
  Prefabs/ Art/ Audio/ Resources/    reserved (empty in this milestone)
```

The assemblies are load-bearing: test asmdefs cannot see predefined assemblies, so the runtime code
lives in `LifeOfEmpires.Runtime` and the tests reference it explicitly — otherwise every test would
pass vacuously against nothing.

## 4. Systems

| Area | File | Notes |
|---|---|---|
| Bootstrap / DI | `Runtime/Core/GameBootstrap.cs`, `ServiceLocator.cs` | composes services in dependency order; owns no gameplay state |
| Battle state | `Runtime/Core/BattleContext.cs` | one instance per battle; rosters, resources, outcome |
| Orchestration | `Runtime/Battle/BattleManager.cs` | spawns per mission, owns victory/defeat wiring, pause via `Time.timeScale` |
| Objectives | `Runtime/Battle/BattleObjectiveManager.cs` | victory/defeat edge-triggered once; stars scored live |
| Economy | `Runtime/Economy/ResourceManager.cs`, `ResourceNode.cs` | refuses negative results, never clamps silently |
| Pathfinding | `Runtime/Pathfinding/PathfindingManager.cs` | grid A*, 8-way with corner-cutting prevention, binary heap, per-frame request budget, per-request node cap, per-goal expansion cache |
| Units | `Runtime/Units/UnitController.cs`, `WorkerAgent.cs`, `UnitSelectionManager.cs` | state machine, steering + separation; gather → drop-off loop; screen-space selection |
| Buildings | `Runtime/Buildings/BuildingController.cs`, `BuildingPlacementSystem.cs` | construction progress, production, ghost preview + footprint validation |
| Combat | `Runtime/Combat/CombatSystem.cs`, `ProjectilePool.cs`, `HealthBar.cs` | single damage funnel (`OnDamaged`/`OnDied`); prewarmed pool — no `Instantiate`/`Destroy` in combat |
| AI | `Runtime/AI/EnemyAIController.cs` | `DevelopEconomy / BuildDefenses / PrepareArmy / Attack / Defend / Recover`, evaluated on a timer |
| Camera | `Runtime/Camera/IsometricCameraRig.cs` | orthographic 45°/45°, pinch zoom clamped, pan clamped, gestures cancel tap detection |
| Input | `Runtime/Input/BattleInputController.cs` | taps/drag/pinch routing; Input System with a legacy fallback |
| Save | `Runtime/Core/SaveSystem.cs` | JSON round-trip; corrupt payload degrades to defaults |
| HUD | `Runtime/UI/BattleHud.cs`, `UIResourcesBar.cs`, `UISelectionPanel.cs`, `UIBuildMenu.cs`, `UIProductionPanel.cs`, `UIResultsPanel.cs`, `SafeAreaFitter.cs` | one uGUI canvas; every control calls an existing system — placement, `TryTrain`, selection, objectives, pause; safe-area anchored, one `EventSystem` |
| Campaign | `Runtime/Campaign/CampaignManager.cs` | consumes `MissionDefinition.unlockRequirement`; drives the results card's Next Mission |

Runtime code references only `UnityEngine`, `Unity.InputSystem` and `UnityEngine.UI` (uGUI). URP is configured exclusively
through generated assets and the editor bootstrap; no runtime file imports
`UnityEngine.Rendering.Universal`.

## 5. Controls

| Gesture | Action |
|---|---|
| Tap a unit | select it (screen-space radius, 72 px — small units stay tappable) |
| Tap the ground with a selection | move order in formation |
| Tap an enemy with a selection | attack order |
| Double-tap a unit | select every unit of the same type within 8 m |
| Drag a box | select combat units in the rectangle (`dragBoxFilter`) |
| Pinch | zoom, clamped to `[7, 26]` ortho size |
| One-finger drag | pan, clamped to the battlefield bounds; never issues a troop order |
| `SelectAllCombatUnits()` / `SelectAll()` / `CenterOnTownHall()` | HUD buttons (`Army` / `Hall`) and programmatic selection commands |
| HUD buttons | `Army`, `Move` (to the camera focus), `Attack` (nearest enemy), `Stop`, `Hall`, build buttons (start placement), train buttons (barracks production), `Pause` |
| Tap your own building | open its production panel, whether or not an army is selected; tapping anywhere else closes it, and the tap issues no move order |

## 6. Data and balance

Every gameplay number is a ScriptableObject under `Assets/LifeOfEmpires/Data/`. Nothing is hardcoded in
runtime code. `GAME_BALANCE.md` lists every value and where it lives; the per-component wiring
defaults (grid size, gesture thresholds, pooling) are in §7 of that document.

## 7. Scope of this milestone

**Implemented:** the full battle loop for two missions — resource gathering and drop-off, worker-driven
construction, unit training, combat with pooled projectiles, pooled-free damage funnel, pathfinding
with budgets and caching, selection and orders, an isometric camera, a timed enemy AI with six
explicit states, victory/defeat, star scoring, crown rewards and a save file. On top of that: an
in-battle HUD (live resource bar, selection panel, build menu, production panel, victory/defeat card),
the ranged `archer` whose shots travel through the projectile pool, and campaign unlock progression
driven by `MissionDefinition.unlockRequirement`.

**Not implemented (planned in later milestones, not silently claimed):** the campaign map / mission
select screen (Milestone 4), the rest of the unit and building roster, skirmish mode, kingdom
progression, achievements, AdMob and IAP integration (`MONETIZATION_SETUP.md`), audio,
prefab-authored art, procedural terrain (the battlefield is a flat plane; `terrainSeed` currently
seeds the AI's determinism), and the iOS device build (`IOS_RELEASE.md`).

**The HUD has never been seen on a device.** Batchmode proves its wiring and behaviour only; safe-area
layout under a real notch, text legibility and touch feel remain unverified.

## 8. Documentation

| File | Contents |
|---|---|
| `DEVELOPMENT_STATUS.md` | what is done, what is partial, known bugs, unverified items, next steps, external accounts |
| `GAME_BALANCE.md` | every balance number and where it lives |
| `MONETIZATION_SETUP.md` | ad/IAP architecture, test identifiers, product ids, publishing workflow |
| `IOS_RELEASE.md` | verified Xcode/Unity versions, the missing iOS module, build steps, App Store checklist |
| `details.md` | the original game specification (input document, not maintained here) |

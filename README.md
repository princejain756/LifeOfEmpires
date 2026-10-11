# Empire Rush

An Age-of-Empires-style real-time strategy game for iPhone, built in Unity 6.6 with URP.
This repository currently contains **Milestones 1 and 2**: the RTS battle — economy, workers,
pathfinding, construction, combat, selection, an isometric camera and a state-machine enemy AI —
with two playable missions' worth of data.

No commits, pushes or deployments are made by the build tooling here.

## 1. Requirements

| Requirement | Value |
|---|---|
| Unity | `6000.6.5f1` (Unity 6.6), arm64 macOS install at `/Applications/Unity/Hub/Editor/6000.6.5f1-arm64` |
| Packages | URP 17.6.0, Input System 1.20.1, Test Framework 1.8.0 (pinned in `Packages/manifest.json` to the versions bundled with this editor) |
| Xcode | 26.3 / iOS SDK 26.2 (**installed**) |
| iOS Build Support module | **NOT installed** — required before an iOS player build; see `IOS_RELEASE.md` §2 |
| Active input handling | Both (`ProjectSettings.asset: activeInputHandler: 2`); runtime input code compiles under `ENABLE_INPUT_SYSTEM` **and** `ENABLE_LEGACY_INPUT_MANAGER` |

## 2. Quick start

```bash
# 1. Regenerate every generated asset: URP assets, quality tier, iOS player settings,
#    ScriptableObject content, materials, and both scenes. Idempotent.
U=/Applications/Unity/Hub/Editor/6000.6.5f1-arm64/Unity.app/Contents/MacOS/Unity
gtimeout 600 "$U" -projectPath "$PWD" -batchmode -quit -nographics \
  -executeMethod EmpireRush.Editor.ProjectBootstrap.Run -logFile /tmp/er_bootstrap.log
# expect: URP_PROBE_OK ... and BOOTSTRAP_OK in the log
```

In the editor: `EmpireRush > Bootstrap Project` runs the same thing, then open
`Assets/EmpireRush/Scenes/Battle.unity` and press Play. `Bootstrap.unity` is the shipped entry
point: it loads `Battle` on start. Both scenes are in `EditorBuildSettings` with `Bootstrap` first.

```bash
# 2. Run the suites
gtimeout 900 "$U" -projectPath "$PWD" -batchmode -nographics \
  -runTests -testPlatform EditMode -testResults /tmp/er_tests.xml -logFile /tmp/er_v3.log
gtimeout 900 "$U" -projectPath "$PWD" -batchmode -nographics \
  -runTests -testPlatform PlayMode -testResults /tmp/er_pm.xml -logFile /tmp/er_pm.log
```

Verified results for this build: **EditMode 52/52 passed**, **PlayMode 1/1 passed**, batch-mode
compile with **0 `error CS`**, and the bootstrap logs `URP_PROBE_OK default=EmpireRush_URP
current=EmpireRush_URP type=UniversalRenderPipelineAsset` followed by `BOOTSTRAP_OK`.

## 3. Layout

```
Assets/EmpireRush/
  Scenes/      Bootstrap.unity (entry point)  Battle.unity (the battle)
  Scripts/Runtime/   game code (EmpireRush.Runtime asmdef)
  Scripts/Editor/    ProjectBootstrap.cs (EmpireRush.Editor asmdef)
  Data/              ScriptableObjects: units, buildings, missions, difficulty, economy
  Art/Materials/     URP/Lit materials generated from the palette
  Tests/EditMode/    unit + system tests (EmpireRush.Tests.EditMode asmdef)
  Tests/PlayMode/    end-to-end battle flow test (EmpireRush.Tests.PlayMode asmdef)
  Settings/          generated URP renderer + pipeline assets
  Prefabs/ Art/ Audio/ Resources/    reserved (empty in this milestone)
```

The assemblies are load-bearing: test asmdefs cannot see predefined assemblies, so the runtime code
lives in `EmpireRush.Runtime` and the tests reference it explicitly — otherwise every test would
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

Runtime code references only `UnityEngine` and `Unity.InputSystem`. URP is configured exclusively
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
| `SelectAllCombatUnits()` / `SelectAll()` / `CenterOnTownHall()` | programmatic selection commands used by the (not yet built) HUD |

## 6. Data and balance

Every gameplay number is a ScriptableObject under `Assets/EmpireRush/Data/`. Nothing is hardcoded in
runtime code. `GAME_BALANCE.md` lists every value and where it lives; the per-component wiring
defaults (grid size, gesture thresholds, pooling) are in §7 of that document.

## 7. Scope of this milestone

**Implemented:** the full battle loop for two missions — resource gathering and drop-off, worker-driven
construction, unit training, combat with pooled projectiles, pooled-free damage funnel, pathfinding
with budgets and caching, selection and orders, an isometric camera, a timed enemy AI with six
explicit states, victory/defeat, star scoring, crown rewards and a save file.

**Not implemented (planned in later milestones, not silently claimed):** archers and the rest of the
unit roster, the 12-mission campaign and mission-select/unlock flow, skirmish mode, kingdom
progression, achievements, AdMob and IAP integration (`MONETIZATION_SETUP.md`), audio, a HUD/UI
canvas, prefab-authored art, procedural terrain (the battlefield is a flat plane; `terrainSeed`
currently seeds the AI's determinism), and the iOS device build (`IOS_RELEASE.md`).

## 8. Documentation

| File | Contents |
|---|---|
| `DEVELOPMENT_STATUS.md` | what is done, what is partial, known bugs, unverified items, next steps, external accounts |
| `GAME_BALANCE.md` | every balance number and where it lives |
| `MONETIZATION_SETUP.md` | ad/IAP architecture, test identifiers, product ids, publishing workflow |
| `IOS_RELEASE.md` | verified Xcode/Unity versions, the missing iOS module, build steps, App Store checklist |
| `details.md` | the original game specification (input document, not maintained here) |

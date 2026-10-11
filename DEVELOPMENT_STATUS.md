# Development Status — Life of Empires

Snapshot of Milestones 1–3 (playable RTS battle with an in-battle HUD) as delivered in this working
tree.

Verified on this machine (macOS 24.6.0 arm64, Unity `6000.6.5f1`):

| Check | Command | Result |
|---|---|---|
| Batch-mode compile | `Unity -projectPath . -batchmode -quit -nographics -logFile /tmp/rv1.log` | **exit 0**, `0` occurrences of `error CS` |
| Bootstrap | `… -executeMethod LifeOfEmpires.Editor.ProjectBootstrap.Run -logFile /tmp/rv2.log` | **exit 0**, log contains `URP_PROBE_OK default=LifeOfEmpires_URP current=LifeOfEmpires_URP type=UniversalRenderPipelineAsset` (line 474) and `BOOTSTRAP_OK` (line 486) |
| EditMode tests | `… -runTests -testPlatform EditMode -testResults /tmp/rv_edit.xml` | **exit 0**, `testcasecount="74" result="Passed"`, 74 passed / 0 failed |
| PlayMode tests | `… -runTests -testPlatform PlayMode -testResults /tmp/rv_play.xml` | **exit 0**, `testcasecount="3" result="Passed"`, 3 passed / 0 failed |

Every number here was observed in those runs; nothing is estimated.

## 1. Completed

- **Project + pipeline.** Unity 6.6 project created by the editor itself (no hand-authored
  `ProjectSettings`). URP 17.6.0 assets (`Settings/LifeOfEmpires_URP.asset`, `LifeOfEmpires_Renderer.asset`)
  are created *and assigned* to `GraphicsSettings.defaultRenderPipeline`; a `Mobile` quality tier is
  bound to the URP asset. iOS player settings applied: `com.princejain756.lifeofempires`, v0.1.0, IL2CPP,
  target iOS 15.0, portrait-only, full screen, hidden status bar.
- **Assemblies.** `LifeOfEmpires.Runtime`, `LifeOfEmpires.Editor`, `LifeOfEmpires.Tests.EditMode`,
  `LifeOfEmpires.Tests.PlayMode`. Runtime code compiles against `UnityEngine` + `Unity.InputSystem`
  + `UnityEngine.UI` (uGUI 2.6.0, declared in `Packages/manifest.json`; `com.unity.modules.physics2d`
  is declared explicitly because it is uGUI's own hard dependency).
- **Two real scenes**, both in `EditorBuildSettings`: `Bootstrap.unity` (loads `Battle`) and
  `Battle.unity` — ground plane in the spec palette `#85A66A`, isometric camera rig, player town
  hall + **player barracks**, 3 workers, 3 trees / 2 farms / 1 gold vein, enemy town hall + barracks
  + swordsman + spearman, all with URP/Lit materials and wired system references (database, mission,
  economy, selection camera, camera rig), plus the **HUD canvas and one `EventSystem`**.
- **Economy.** `ResourceManager` with a single spend path; `EconomyBalance` data asset drives
  gathering, carry capacity, deposit time, construction rate. Negative results are refused, never
  clamped silently.
- **Pathfinding.** Grid A* over `bool[] blocked`: 8-way with corner-cutting prevention, binary-heap
  open set, per-frame request budget (`maxPathRequestsPerFrame`, default 4), per-request node cap
  (`maxExpandedNodesPerRequest`, default 4000), per-goal expansion cache with grid-version
  invalidation, goal snapping, and a completion delegate that always fires — an impossible request
  reports a reason instead of hanging.
- **Units.** `UnitController` state machine (Idle/Moving/Gathering/Attacking/Dead) with waypoint
  steering and neighbour separation; `WorkerAgent` gather → deposit → repeat loop that aborts
  cleanly on depletion or reassignment; scene-authored workers are bound to a gather node and a
  drop-off automatically at battle start.
- **Selection.** Screen-space (not raycast) tap selection with a 72 px radius, drag-box selection
  with a type filter, double-tap same-type selection, select-all / select-all-combat / centre-on-town-hall
  commands, and orders (move in formation, attack-move). Selection requires an assigned camera; it
  never substitutes an arbitrary `Camera.main`.
- **Buildings.** Health, construction progress, production queue, grid footprint occupancy;
  `BuildingPlacementSystem` with ghost preview, valid/invalid tint, footprint + terrain + cost
  validation, and confirm-on-tap while a ghost is active.
- **Combat.** One damage funnel (`CombatSystem.ApplyDamage`) raising `OnDamaged`/`OnDied`;
  a prewarmed `ProjectilePool` (no `Instantiate`/`Destroy` anywhere in runtime code); contextual
  world-space billboard `HealthBar`.
- **AI.** `EnemyAIController` with the six states `DevelopEconomy / BuildDefenses / PrepareArmy /
  Attack / Defend / Recover`, evaluated on a 2 s timer (never per frame), threat detection by radius,
  town-hall-below-30 % recovery, army-threshold attacks. Fully offline; no API calls.
- **Camera + input.** Orthographic rig at pitch/yaw 45°, pinch zoom clamped to `[7, 26]`, pan clamped
  to battlefield bounds, and a dedicated gesture layer that suppresses tap detection once a press
  moves past 22 px — so a pinch or pan can never issue a troop order.
- **Battle flow.** `BattleManager` spawns per mission, `BattleObjectiveManager` resolves victory
  (enemy town hall destroyed) and defeat (player town hall destroyed) exactly once, scores the
  mission's stars against live world state, writes the outcome back to `BattleContext`, and
  `Time.timeScale = 0` pause. Victory persists a mission record and awards the mission's crowns.
- **Save.** JSON round-trip of mission records, crowns, entitlements and purchased products; corrupt
  or truncated payloads fall back to defaults instead of throwing.
- **In-battle HUD (`Scripts/Runtime/UI/`).** One uGUI canvas, authored into `Battle.unity` by the
  bootstrap and rebuilt identically at runtime if it is ever absent:
  `UIResourcesBar` (live wood/food/gold from `ResourceManager.OnResourceChanged`, crowns read from
  the save file because `BattleManager.HandleBattleEnded` is what writes them), `UISelectionPanel`
  (count, unit name, aggregate health, plus Move/Attack/Stop/Army/Hall wrappers over
  `UnitSelectionManager` and `UnitController`), `UIBuildMenu` (one priced button per non-town-hall
  building, disabled until affordable, calling `BuildingPlacementSystem.BeginPlacement` only —
  the input controller still owns the ghost and the confirm tap), `UIProductionPanel` (training
  progress plus one button per combat unit, routed through `BuildingController.TryTrain` and
  surfacing its refusal reason), `UIResultsPanel` (victory/defeat, stars, the mission's crown reward,
  Return and Next Mission), and a Pause button. `CanvasScaler` is `ScaleWithScreenSize`, matched on
  width, 1080×1920 portrait, with a `SafeAreaFitter` holding every control inside `Screen.safeArea`.
  Exactly one `EventSystem` with `InputSystemUIInputModule` — no legacy module, so no double-fired
  taps.
- **Ranged unit.** `archer` (70 HP, 11 damage, 1.4 s interval, 9 m range, `isRanged: true`, 40/30/10,
  14 s) exists as `Data/Unit_Archer.asset` and is trainable from the barracks through
  `BuildingController.TryTrain`, so the pooled-projectile branch runs in gameplay rather than only
  under direct calls.
- **`BuildingController.TryTrain`.** Trains any unit a building may field, not only its single
  `producesUnitId`; delegates to `EnqueueTraining` for that one and otherwise charges once through
  `ResourceManager.TrySpend` after every precondition passes. The barracks keeps one production slot
  and one charge path, and the AI is untouched.
- **Campaign progression.** `CampaignManager` consumes `MissionDefinition.unlockRequirement`
  (`IsUnlocked`, `NextPlayableMission`, `StarLabel`); the results card's Next Mission button parks the
  chosen mission and `GameBootstrap` picks it up when it composes the next battle.
- **Content.** 4 unit definitions, 3 building definitions, 2 missions, 3 difficulty profiles, 1 economy
  balance — all as ScriptableObjects (`GAME_BALANCE.md`).
- **Tests.** 74 EditMode tests (economy arithmetic, cost/definition agreement, pathfinding behaviour
  and budgets, selection, objectives, save system, shipped-asset integrity, campaign unlock rules,
  archer projectile pooling, the production refund path, HUD behaviour, and where a tap on the player's
  own building is routed) and 3 PlayMode tests: the first
  builds a barracks through the real placement flow, waits for a dispatched worker to finish it, trains
  a swordsman and razes an enemy tower; the second trains an **archer** through the multi-unit
  production path and confirms the ranged unit that leaves the barracks; the third places an enemy in an
  archer's reach and lets the unit's own tick fire, proving the shot travels through the prewarmed
  `ProjectilePool` (`ActiveCount > 0` in flight, `TotalCreated` unchanged, `RefusedCount == 0`, damage on
  impact) rather than being applied directly.

## 2. Partially completed

- **The HUD has no campaign map behind it.** The results card can start the next unlocked mission
  directly, but there is no mission-select screen: the campaign map is Milestone 4 and no scene for it
  exists yet.
- **Building selection is HUD-local.** Tapping a player building opens its production panel; the
  selection manager itself still only knows about units, and the panel lives on the HUD. One tap means
  one thing: `BattleInputController` resolves a tap that lands on one of the player's own buildings
  through the same `BattleContext.FindPlayerBuildingNearScreenPoint` helper the HUD uses and leaves the
  panel to claim it, so the tap is neither a move order nor an attack order. The panel therefore opens
  and closes on building taps alone and can never be locked away by having an army selected
  (`TapRoutingTests`).
- **`terrainSeed` seeds determinism, not terrain.** It is passed to the enemy AI's random source.
  The battlefield is a flat plane; procedural terrain generation belongs to a later milestone.
- **Art and audio are primitives and silence.** Units/buildings are Unity primitives with generated
  URP/Lit materials; the HUD is flat colour blocks and the one builtin font (`LegacyRuntime.ttf`).
  `Audio/`, `Prefabs/` and `Resources/` are reserved and empty.

## 3. Known bugs

No known defect is open in the delivered systems: the 74 EditMode and 3 PlayMode tests pass, the
batch-mode compile is clean, and the bootstrap completes. Items in §2 are missing features, not
malfunctions. Two design limits are worth calling out explicitly:

- **Single-player determinism is not guaranteed across machines.** The AI uses `Random.Range` seeded
  per mission (`terrainSeed`); nothing else in the runtime is a source of nondeterminism, but no
  cross-machine deterministic-run test exists.
- **The per-frame pathfinding budget is a soft guarantee.** `maxPathRequestsPerFrame` bounds searches
  per call to `ProcessPendingRequests`, and each search is additionally capped at 4000 expansions;
  a request that needs a full 4000-expansion search still costs that much on the frame it is served.
  With the default 64×64 grid the cap is never approached in the shipped missions.

### Defects found and fixed while verifying this build

Recorded because each was a real, observable failure, not a refactor:

1. `PathfindingManager` discarded every node whose heap priority exceeded its g-score, so no search
   ever expanded beyond the start cell and **every** path request returned `Unreachable`
   (`LastExpandedNodes == 0`). The staleness test now compares against `g + h`.
2. `PathfindingManager.TryReuseCache` walked the came-from chain *forwards* from the new start, which
   runs away from the goal and dead-ends; cached routes now walk back from the goal and reverse.
3. `HealthBar.Bind` wrote `verticalOffset` onto its own transform, and the bar shares its owner's
   GameObject — so **every unit in the project was teleported to `(0, 1.6, 0)`**, which is why all
   units piled at the origin and drag-box/double-tap selection misfired. The offset now goes on the
   bar's child root.
4. `Destroy` was called at construction time in unit rings, health-bar quads, pooled projectiles,
   placement ghosts and instance materials; that is illegal outside play mode and failed five tests
   with "Destroy may not be called from edit mode". All such calls now use `DestroyImmediate` outside
   play mode.
5. `BattleContext.TownHallOf` returned a building until its own death handler had run, but observers
   on the same death event can run first — victory/defeat could stay `InProgress`. Liveness is now
   health-based (`IsAlive`).
6. `ProjectBootstrap` passed a `GameDatabase` reference captured *before* `EditorSceneManager.NewScene`
   into the scene builders. The scene swap unloads that asset, so every `FindUnit`/`FindBuilding`
   returned null and **the battle scene was saved with no units and no buildings**. Each scene
   builder now reloads the database after the swap; `Battle.unity` now contains the town halls,
   workers and enemy camp, with `GameBootstrap.database` assigned.
7. `SaveData.RecordMissionResult` only recorded a best time for completed runs, so a first timed
   attempt was overwritten by a later slower one. The fastest recorded run is now kept.
8. One EditMode assertion contradicted the line below it (`SelectAll()` was expected to return 2
   while the very next assertion expected 4 selected units); corrected to 4.
9. The PlayMode suite leaked objects between tests. `BattleManager`/`BuildingPlacementSystem` spawn
   units and buildings themselves, so anything they created escaped the test's own tracked list — a
   live worker from the first test was adopted by the second test's `AdoptSceneContents` and, holding
   a reference to the destroyed navigation grid of the first test, failed the second test's build
   order as "unreachable". `TearDown` now destroys surviving units, buildings and resource nodes.
10. Driving a pooled projectile's private `Update` callback through `SendMessage` from an EditMode
   test asserts `ShouldRunBehaviour()` inside the editor; the archer test now invokes the callback by
   reflection instead, which the engine's behaviour runner does not intercept.

## 4. Unverified functionality

Nothing below has been observed to work, and none of it is claimed:

- **iOS device build, simulator build, TestFlight, App Store submission.** Blocked: the Unity **iOS
  Build Support** module is not installed on this machine (`PlaybackEngines/` holds only
  `MacStandaloneSupport` and `WebGLSupport`). Xcode 26.3 / iOS SDK 26.2 *are* installed and satisfy
  Apple's October 2026 requirement — see `IOS_RELEASE.md`.
- **On-device performance.** Frame rate, GC spiking, memory ceiling, thermal behaviour, IL2CPP/AOT
  build time, and battery impact on a real iPhone.
- **Touch feel on hardware.** Tap forgiveness, pinch-vs-pan disambiguation, and one-handed reach have
  only been reasoned about and unit-tested in screen space (72 px radius, 22 px drag threshold,
  90 px double-tap drift) — never felt on a device.
- **The HUD on a device.** The HUD has **never been seen on a device**. Batchmode proves its wiring
  and behaviour — labels follow the stockpile, buttons reach the right systems — and the layout is
  anchored against `Screen.safeArea`, but safe-area handling under a real notch/Dynamic Island, text
  legibility, button hit-size and touch feel have not been observed. Batchmode has no display at all,
  so `SafeAreaFitter` is a no-op there.
- **Advertising and in-app purchase.** No SDK is integrated and no store interaction has occurred
  (`MONETIZATION_SETUP.md`).
- **Campaign progression beyond one hop.** The unlock rule and Next Mission button are unit-tested,
  and the unlock rule is exercised in PlayMode/EditMode, but no run has been watched through two
  consecutive missions in a built player.
- **Procedural terrain and audio** — not implemented, therefore not verified.

## 5. Next steps

1. Campaign map scene (Milestone 4): mission list driven by `CampaignManager.IsUnlocked`/`StarLabel`,
   replacing the results card's direct Next Mission hop.
2. Terrain generation from `terrainSeed` (height + obstacles fed into `PathfindingManager.SetBlockedRect`
   and terrain costs), plus fog/decoration pass.
3. Audio: gather, build, combat and UI cues; music loop.
4. Ad/IAP integration behind the `AdvertisingService` abstraction, with Google test ad units until
   the app is published.
5. iOS: install the Hub `ios` module, produce the Xcode project, profile on device, then iterate on
   HUD safe-area layout and touch feel.

## 6. Required external account configuration

- **Apple Developer Program** membership; a signing team for `com.princejain756.lifeofempires`.
- **App Store Connect**: bundle id registered, Agreements/Tax/Banking complete, three non-consumable
  products created with the identifiers in `MONETIZATION_SETUP.md`, screenshots and metadata.
- **Google AdMob**: account, app registration for the iOS bundle id, one rewarded ad unit; the App ID
  and unit id fed to the advertising service at build time (never committed); `app-ads.txt` hosted on
  the developer domain; AdMob app-readiness review completed (real ads only serve once the app is
  published and approved).
- **Privacy**: App Store privacy nutrition labels for the ad SDK, and tracking authorisation if
  personalised advertising is ever enabled.

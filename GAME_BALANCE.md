# Game Balance — Empire Rush

Every number that affects gameplay, and where it lives. **No balance number is hardcoded in runtime
code**: units, buildings, missions, difficulty and economy knobs are ScriptableObject assets under
`Assets/EmpireRush/Data/`. Component fields in `Scripts/Runtime` are wiring defaults (grid size,
gesture thresholds, pooling) — they are listed in section 7 for completeness.

Rebuild/refresh the assets with `EmpireRush/Bootstrap Project` (or
`-executeMethod EmpireRush.Editor.ProjectBootstrap.Run`); the bootstrap writes the values below.

## 1. Units — `Data/Unit_*.asset` (`UnitDefinition`)

| Field | Worker | Swordsman | Spearman |
|---|---|---|---|
| `id` | `worker` | `swordsman` | `spearman` |
| `maxHealth` | 40 | 110 | 95 |
| `damage` | 0 | 12 | 9 |
| `attackInterval` (s) | 1.0 | 1.0 | 1.2 |
| `attackRange` (m) | 1.2 | 1.6 | 2.1 |
| `moveSpeed` (m/s) | 3.6 | 3.2 | 3.2 |
| `isRanged` | false | false | false |
| `counters` | — | `archer` | — |
| `populationCost` | 1 | 2 | 2 |
| `cost` wood / food / gold / crowns | 50 / 0 / 0 / 0 | 0 / 60 / 20 / 0 | 30 / 40 / 10 / 0 |
| `trainSeconds` | 8 | 12 | 11 |
| `color` (RGBA) | 0.85, 0.78, 0.62 | 0.62, 0.66, 0.74 | 0.70, 0.58, 0.42 |

Derived rule (code, `UnitDefinition.IsCombatUnit`): a unit is a combat unit when `damage > 0` or it
declares at least one `counters` entry. The worker is therefore not a combat unit and is excluded
from army selection and attack-move.

Archers are **not** implemented yet; the swordsman's `archer` counter is data waiting for Milestone 3.

## 2. Buildings — `Data/Building_*.asset` (`BuildingDefinition`)

| Field | Town Hall | Barracks | Watchtower |
|---|---|---|---|
| `id` | `town_hall` | `barracks` | `watchtower` |
| `maxHealth` | 900 | 450 | 320 |
| `cost` wood / food / gold / crowns | 0 / 0 / 0 / 0 | 120 / 0 / 40 / 0 | 80 / 0 / 60 / 0 |
| `buildSeconds` | 30 | 20 | 15 |
| `footprint` (x, y) | 3 × 3 | 3 × 3 | 2 × 2 |
| `producesUnitId` | `worker` | `swordsman` | — |
| `popProvided` | 10 | 5 | 0 |
| `isTownHall` | true | false | false |

The town hall is free because the mission grants it at start; it is also the defeat condition.
`buildSeconds` is the *authored* duration; actual build time is driven by workers (section 4).

## 3. Missions — `Data/Mission_*.asset` (`MissionDefinition`)

| Field | `Mission_01_FirstSettlement` | `Mission_02_EnemyCamp` |
|---|---|---|
| `title` | First Settlement | The Enemy Camp |
| `terrainSeed` | 20261011 | 20261012 |
| `startingWood / Food / Gold` | 200 / 150 / 50 | 250 / 180 / 70 |
| `enemyUnitIds` | `worker`, `swordsman`, `swordsman` | `worker`, `worker`, `swordsman`, `swordsman`, `spearman`, `spearman` |
| `enemyStartDelaySeconds` | 25 | 20 |
| `difficulty` | `Difficulty_Normal` | `Difficulty_Hard` |
| `victoryCondition` | `DestroyEnemyTownHall` (1) | `DestroyEnemyTownHall` (1) |
| `defeatCondition` | `LosePlayerTownHall` (3) | `LosePlayerTownHall` (3) |
| `crownReward` | 2 | 3 |
| `unlockRequirement` | — | `mission_01` |
| stars | 1. Destroy the enemy town hall (threshold 1)<br>2. Train 8 units<br>3. Stockpile 250 gold | 1. Destroy the enemy town hall (threshold 1)<br>2. Destroy 2 enemy structures<br>3. Train 12 units |

Each star carries `crownValue: 1`. Star conditions are scored live against the battle
(`BattleObjectiveManager.ScoreStars`): `DestroyEnemyTownHall`, `DestroyEnemyBuildings`,
`TrainUnits`, `StockpileResource`. Any other condition is reported once as unsupported and is never
awarded.

The mission's `startingWood/Food/Gold` override `EconomyBalance.startingResources` for the player;
the AI always starts from `EconomyBalance.startingResources`.

## 4. Economy — `Data/EconomyBalance.asset` (`EconomyBalance`)

| Field | Value | Used by |
|---|---|---|
| `startingResources` (wood/food/gold/crowns) | 200 / 150 / 50 / 0 | `GameBootstrap`, AI opening stockpile |
| `gatherIntervalSeconds` | 1.5 | `WorkerAgent` gather tick |
| `gatherAmountPerTick` | 5 | resources removed from a node per tick |
| `workerCarryCapacity` | 20 | worker trip size (4 ticks) |
| `depositSeconds` | 0.35 | hand-over pause at the drop-off |
| `basePopulationCap` | 10 | population ceiling before town halls are counted |
| `buildProgressPerWorkerSecond` | 0.25 | construction progress per builder-second |

Effective build time = `buildSeconds / (workers × buildProgressPerWorkerSecond)`: a barracks (20 s)
therefore needs 80 builder-seconds, i.e. 80 s with one worker, 40 s with two.

All spending goes through one place: `EconomyBalance.CanAfford/TrySpend/Spend` plus
`ResourceManager` (`Add`, `TrySpend(cost, out reason)`, `CanAfford`). `ResourceManager` refuses
negative results and never clamps silently — an unaffordable spend mutates nothing.

## 5. Difficulty — `Data/Difficulty_*.asset` (`DifficultyProfile`)

| Field | Easy | Normal | Hard |
|---|---|---|---|
| `decisionIntervalSeconds` | 2.5 | 2.0 | 1.5 |
| `workerCount` | 2 | 3 | 4 |
| `productionRateMultiplier` | 0.75 | 1.0 | 1.35 |
| `armySize` (attack threshold) | 3 | 4 | 6 |
| `attackCooldownSeconds` | 35 | 25 | 18 |
| `threatRadius` (m) | 16 | 18 | 22 |
| `recoverHealthFraction` | 0.35 | 0.30 | 0.25 |

Difficulty scales **decision pace, economy, and army thresholds only**. It never inflates health or
damage: a hard swordsman has the same 110 HP as an easy one, so the player's unit knowledge stays
true across the whole campaign.

## 6. Population

`UnitDefinition.populationCost` is charged against the owning side's capacity
(`BuildingDefinition.popProvided` + `EconomyBalance.basePopulationCap`).

| Unit | Population | | Building | Provides |
|---|---|---|---|---|
| Worker | 1 | | Town Hall | 10 |
| Swordsman | 2 | | Barracks | 5 |
| Spearman | 2 | | Watchtower | 0 |

## 7. Wiring defaults (component fields, not balance assets)

These live in `Scripts/Runtime` and are set per scene by the bootstrap. They are listed so a reader
never has to guess where a number came from.

| Component | Field | Default |
|---|---|---|
| `BattleManager` | `gridWidth` / `gridHeight` / `cellSize` | 64 / 64 / 1 m |
| `BattleManager` | `spawnMissionEnemies` | true |
| `PathfindingManager` | `width` / `height` / `cellSize` | 64 / 64 / 1 m |
| `PathfindingManager` | `maxPathRequestsPerFrame` | 4 |
| `PathfindingManager` | `maxExpandedNodesPerRequest` | 4000 |
| `PathfindingManager` | `goalSnapRadius` | 4 cells |
| `PathfindingManager` | `maxCachedGoals` | 64 |
| `IsometricCameraRig` | `pitch` / `yaw` | 45° / 45° (true isometric) |
| `IsometricCameraRig` | `minOrthoSize` / `maxOrthoSize` / `startOrthoSize` | 7 / 26 / 14 m |
| `IsometricCameraRig` | `pinchSensitivity` | 0.045 m per pixel |
| `IsometricCameraRig` | `cameraDistance` | 60 m |
| `IsometricCameraRig` | `panMargin` | 4 m beyond battlefield bounds |
| `UnitSelectionManager` | `tapScreenRadius` | 72 px |
| `UnitSelectionManager` | `doubleTapMaxDelay` / `doubleTapMaxDrift` / `doubleTapSearchRadius` | 0.35 s / 90 px / 8 m |
| `UnitSelectionManager` | `dragBoxFilter` | `CombatOnly` |
| `BattleInputController` | `tapMovementThresholdPixels` (drag cancels a tap) | 22 px |
| `BattleInputController` | `dragBoxMinSizePixels` | 28 px |
| `UnitController` | `waypointTolerance` / `separationRadius` / `maxSeparationNeighbours` | 0.35 m / 0.9 m / 8 |
| `UnitController` | `aggroRadius` | 7 m |
| `UnitController` | `selectionRingScale` / `deathFadeSeconds` | 1.35 / 0.15 s |
| `WorkerAgent` | `searchRadius` | 80 m |
| `ResourceNode` | `interactionRadius` / `destroyWhenDepleted` | 1.6 m / true |
| `ProjectilePool` | `prewarmCount` / `maxSize` | 64 / 256 |
| `ProjectilePool` | `speed` / `maxLifetime` / `visualScale` | 18 m/s / 4 s / 0.28 |
| `HealthBar` | `width` / `height` / `verticalOffset` / `lingerSeconds` | 0.9 / 0.12 / 1.6 m / 3 s |
| `EnemyAIController` | `evaluationInterval` | 2 s (timer, not per-frame) |
| `EnemyAIController` | `productionIntervalSeconds` | 9 s |
| `EnemyAIController` | `armySize` / `workerCount` / `threatRadius` / `attackCooldownSeconds` | 4 / 3 / 18 m / 25 s (overridden by the mission's `DifficultyProfile`) |
| `BuildingPlacementSystem` | `maxBuildableTerrainCost` | 1.01 (flat ground only) |

## 8. Scene contents (Battle.unity, built by the bootstrap)

| Item | Position (x, z) | Node capacity / notes |
|---|---|---|
| Ground plane | 32, 32 | 64 × 64 m, palette `#85A66A` (`0.522, 0.651, 0.416`) |
| Player town hall | 9, 9 | — |
| Workers ×3 | 6, 12 · 12, 6 · 13, 13 | — |
| Trees ×3 (`Tree_1..3`) | 4, 8 · 5, 12 · 15, 10 | 120 wood each |
| Farms ×2 | 16, 5 · 6, 16 | 180 food each |
| Gold vein | 18, 16 | 90 gold |
| Enemy town hall | 54, 54 | — |
| Enemy barracks | 48, 52 | — |
| Enemy swordsman / spearman | 51, 50 · 53, 49 | — |

## 9. Render/quality numbers

| Setting | Value | Where |
|---|---|---|
| URP asset / renderer | `Settings/EmpireRush_URP.asset`, `Settings/EmpireRush_Renderer.asset` | bootstrap |
| URP `shadowDistance` | 40 | URP asset |
| URP MSAA (`msaaSampleCount`) | 1 | URP asset — URP ignores the quality tier's anti-aliasing setting |
| URP HDR / `renderScale` | off / 1 | URP asset |
| Quality tier `Mobile` | created and bound to the URP asset | `QualitySettings` |
| Grass colour | `#85A66A` | `ProjectBootstrap.GrassColor` |

## 10. Save data

`SaveData` (`Scripts/Runtime/Core/SaveSystem.cs`) holds `crowns`, one `MissionRecord` per touched
mission (`stars`, `completed`, `bestSeconds`, `timesPlayed`), `entitlements`, and
`purchasedProducts`. `bestSeconds` is the **fastest recorded run** for that mission, whether or not
that run was a win; a slower later run never overwrites it.

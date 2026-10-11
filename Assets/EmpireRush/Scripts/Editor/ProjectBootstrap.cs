using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EmpireRush.Editor
{
    /// <summary>
    /// Creates every generated asset this project needs: the URP pipeline assets, the Mobile quality
    /// tier, the iOS player settings, the ScriptableObject balance/content assets, and the two scenes.
    ///
    /// It exists because none of this can be hand-authored reliably. ProjectSettings YAML and URP
    /// asset GUID wiring are exactly the things that silently break a project, and the engine is the
    /// only authority on their shape.
    ///
    /// Run it from the menu (<c>EmpireRush/Bootstrap Project</c>) or headlessly:
    /// <c>Unity -batchmode -quit -executeMethod EmpireRush.Editor.ProjectBootstrap.Run</c>
    /// </summary>
    public static class ProjectBootstrap
    {
        private const string Root = "Assets/EmpireRush";
        private const string SettingsFolder = Root + "/Settings";
        private const string DataFolder = Root + "/Data";
        private const string ScenesFolder = Root + "/Scenes";
        private const string ArtFolder = Root + "/Art/Materials";

        private const string UrpAssetPath = SettingsFolder + "/EmpireRush_URP.asset";
        private const string UrpRendererPath = SettingsFolder + "/EmpireRush_Renderer.asset";
        private const string DatabasePath = DataFolder + "/GameDatabase.asset";
        private const string BattleScenePath = ScenesFolder + "/Battle.unity";
        private const string BootstrapScenePath = ScenesFolder + "/Bootstrap.unity";
        private const string MobileQualityName = "Mobile";

        /// <summary>Spec palette: the battlefield ground colour.</summary>
        private static readonly Color GrassColor = new Color(133f / 255f, 166f / 255f, 106f / 255f);
        private static readonly Color BarkColor = new Color(0.36f, 0.26f, 0.17f);
        private static readonly Color LeafColor = new Color(0.24f, 0.45f, 0.22f);
        private static readonly Color FarmColor = new Color(0.79f, 0.68f, 0.29f);
        private static readonly Color GoldColor = new Color(0.85f, 0.71f, 0.22f);
        private static readonly Color BuildingColor = new Color(0.72f, 0.66f, 0.55f);
        private static readonly Color EnemyBuildingColor = new Color(0.58f, 0.42f, 0.40f);

        [MenuItem("EmpireRush/Bootstrap Project")]
        public static void Run()
        {
            try
            {
                // The Settings folder must exist before any CreateAsset call lands in it — without it
                // AssetDatabase.CreateAsset throws and the URP wiring never happens.
                EnsureFolder(Root, "Settings");
                EnsureFolder(Root, "Data");
                EnsureFolder(Root, "Scenes");
                EnsureFolder(Root, "Art");
                EnsureFolder(Root + "/Art", "Materials");

                RenderPipelineAsset urpAsset = CreateRenderPipelineAssets();
                GameDatabase database = CreateContentAssets();
                ConfigureRenderPipeline(urpAsset);
                if (urpAsset is UniversalRenderPipelineAsset universal) ConfigureRenderPipelineSettings(universal);
                ConfigureQualityTiers(urpAsset);
                ConfigurePlayerSettings();
                BuildScenes(database);
                ConfigureBuildSettings();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                VerifyRenderPipeline();

                Debug.Log("BOOTSTRAP_OK");
            }
            catch (Exception exception)
            {
                Debug.LogError($"BOOTSTRAP_FAILED: {exception}");
                throw;
            }
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{child}")) AssetDatabase.CreateFolder(parent, child);
        }

        // -----------------------------------------------------------------------------------------
        // Render pipeline
        // -----------------------------------------------------------------------------------------

        private static RenderPipelineAsset CreateRenderPipelineAssets()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UrpRendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                rendererData.name = "EmpireRush_Renderer";
                AssetDatabase.CreateAsset(rendererData, UrpRendererPath);
            }

            var pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (pipelineAsset == null)
            {
                pipelineAsset = UniversalRenderPipelineAsset.Create(rendererData);
                pipelineAsset.name = "EmpireRush_URP";
                AssetDatabase.CreateAsset(pipelineAsset, UrpAssetPath);
            }

            EditorUtility.SetDirty(rendererData);
            EditorUtility.SetDirty(pipelineAsset);
            Debug.Log($"ProjectBootstrap: URP assets ready at '{UrpAssetPath}' with renderer '{UrpRendererPath}'.");
            return pipelineAsset;
        }

        /// <summary>
        /// Configure the URP asset itself. Under a Scriptable Render Pipeline Unity ignores
        /// QualitySettings.shadowDistance and QualitySettings.antiAliasing — the pipeline asset owns
        /// both — so a tier that only sets those leaves shadows and MSAA effectively unconfigured.
        /// </summary>
        private static void ConfigureRenderPipelineSettings(UniversalRenderPipelineAsset urpAsset)
        {
            urpAsset.shadowDistance = 40f;
            urpAsset.msaaSampleCount = 1; // 1 = MSAA off: the mobile-first default for this project
            urpAsset.supportsHDR = false;
            urpAsset.renderScale = 1f;
            EditorUtility.SetDirty(urpAsset);

            Debug.Log($"ProjectBootstrap: URP asset settings — shadowDistance={urpAsset.shadowDistance}, msaaSampleCount={urpAsset.msaaSampleCount}, supportsHDR={urpAsset.supportsHDR}, renderScale={urpAsset.renderScale}.");
        }

        private static void ConfigureRenderPipeline(RenderPipelineAsset urpAsset)
        {
            // Both the forward-looking default and the legacy field are set: GraphicsSettings is the
            // reason a project can look wired up in the Inspector yet render with the built-in
            // pipeline, and leaving either null is how that happens.
            GraphicsSettings.defaultRenderPipeline = urpAsset;

#pragma warning disable 618, 619
            GraphicsSettings.renderPipelineAsset = urpAsset; // obsolete in Unity 6 but still consulted
#pragma warning restore 618, 619

            AssetDatabase.SaveAssets();
        }

        private static void VerifyRenderPipeline()
        {
            RenderPipelineAsset current = GraphicsSettings.currentRenderPipeline;
            if (current == null)
            {
                throw new InvalidOperationException("GraphicsSettings.currentRenderPipeline is null after assignment — the URP asset is not active.");
            }

            Debug.Log($"URP_PROBE_OK default={GraphicsSettings.defaultRenderPipeline?.name ?? "NULL"} current={current.name} type={current.GetType().Name}");
        }

        // -----------------------------------------------------------------------------------------
        // Quality tiers
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Append a "Mobile" quality tier, configured for a phone: short shadows, no MSAA, vsync on,
        /// and bound to the URP asset. The per-platform default for iPhone is left untouched at
        /// index 2.
        /// </summary>
        private static void ConfigureQualityTiers(RenderPipelineAsset urpAsset)
        {
            UnityEngine.Object[] settingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (settingsAssets == null || settingsAssets.Length == 0)
            {
                throw new InvalidOperationException("Could not load ProjectSettings/QualitySettings.asset to add the Mobile tier.");
            }

            var serialized = new SerializedObject(settingsAssets[0]);
            SerializedProperty levels = serialized.FindProperty("m_QualitySettings");
            if (levels == null || !levels.isArray)
            {
                throw new InvalidOperationException("QualitySettings.asset has no m_QualitySettings array.");
            }

            int index = FindLevelIndex(levels, MobileQualityName);
            if (index < 0)
            {
                index = levels.arraySize;
                levels.InsertArrayElementAtIndex(index);
                SerializedProperty appended = levels.GetArrayElementAtIndex(index);
                if (appended == null)
                {
                    throw new InvalidOperationException("Failed to append a quality level.");
                }
            }

            SerializedProperty level = levels.GetArrayElementAtIndex(index);
            SetString(level, "name", MobileQualityName);
            SetFloat(level, "shadowDistance", 40f);
            SetInt(level, "antiAliasing", 1);
            SetInt(level, "vSyncCount", 1);
            SetInt(level, "shadows", 2);

            SerializedProperty pipeline = level.FindPropertyRelative("customRenderPipeline");
            if (pipeline != null)
            {
                pipeline.objectReferenceValue = urpAsset;
            }
            else
            {
                Debug.LogWarning("ProjectBootstrap: no customRenderPipeline field on the quality level; the URP asset is bound through GraphicsSettings only.");
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            QualitySettings.SetQualityLevel(index, true);

#pragma warning disable 618, 619
            QualitySettings.renderPipeline = urpAsset;
            QualitySettings.shadowDistance = 40f;
            QualitySettings.antiAliasing = 1;
            QualitySettings.vSyncCount = 1;
#pragma warning restore 618, 619

            ValidateQualityTier(index);
        }

        private static void ValidateQualityTier(int index)
        {
            string[] names = QualitySettings.names;
            string actual = index >= 0 && index < names.Length ? names[index] : "<out of range>";
            if (actual != MobileQualityName)
            {
                throw new InvalidOperationException($"Quality tier {index} is '{actual}', expected '{MobileQualityName}'.");
            }

            RenderPipelineAsset bound = QualitySettings.renderPipeline;
            Debug.Log($"ProjectBootstrap: quality tier {index} = '{names[index]}', shadowDistance={QualitySettings.shadowDistance}, antiAliasing={QualitySettings.antiAliasing}, vSyncCount={QualitySettings.vSyncCount}, pipeline={bound?.name ?? "NULL"}. Under URP the tier's shadowDistance and antiAliasing are ignored by Unity; the URP asset carries the effective values.");
        }

        private static int FindLevelIndex(SerializedProperty levels, string name)
        {
            for (int i = 0; i < levels.arraySize; i++)
            {
                SerializedProperty element = levels.GetArrayElementAtIndex(i);
                SerializedProperty nameProperty = element.FindPropertyRelative("name");
                if (nameProperty != null && nameProperty.stringValue == name) return i;
            }

            return -1;
        }

        private static void SetString(SerializedProperty parent, string field, string value)
        {
            SerializedProperty property = parent.FindPropertyRelative(field);
            if (property != null) property.stringValue = value;
        }

        private static void SetFloat(SerializedProperty parent, string field, float value)
        {
            SerializedProperty property = parent.FindPropertyRelative(field);
            if (property != null) property.floatValue = value;
        }

        private static void SetInt(SerializedProperty parent, string field, int value)
        {
            SerializedProperty property = parent.FindPropertyRelative(field);
            if (property != null) property.intValue = value;
        }

        // -----------------------------------------------------------------------------------------
        // Player settings
        // -----------------------------------------------------------------------------------------

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "EmpireRush";
            PlayerSettings.productName = "EmpireRush";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.empirerush.kingdom");
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);

            PlayerSettings.iOS.targetOSVersionString = "15.0";

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            PlayerSettings.iOS.requiresFullScreen = true;
            PlayerSettings.statusBarHidden = true;

            AssetDatabase.SaveAssets();
            Debug.Log($"ProjectBootstrap: player settings applied — {PlayerSettings.companyName}/{PlayerSettings.productName} v{PlayerSettings.bundleVersion}, iOS target {PlayerSettings.iOS.targetOSVersionString}, orientation {PlayerSettings.defaultInterfaceOrientation}.");
        }

        // -----------------------------------------------------------------------------------------
        // Content assets
        // -----------------------------------------------------------------------------------------

        private static GameDatabase CreateContentAssets()
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath);
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<GameDatabase>();
                database.name = "GameDatabase";
                AssetDatabase.CreateAsset(database, DatabasePath);
            }

            database.economy = CreateOrLoad<EconomyBalance>("EconomyBalance", economy =>
            {
                economy.startingResources = new ResourceBundle(200, 150, 50, 0);
                economy.gatherIntervalSeconds = 1.5f;
                economy.gatherAmountPerTick = 5;
                economy.workerCarryCapacity = 20;
                economy.depositSeconds = 0.35f;
                economy.basePopulationCap = 10;
                economy.buildProgressPerWorkerSecond = 0.25f;
            });

            DifficultyProfile easy = CreateOrLoad<DifficultyProfile>("Difficulty_Easy", profile =>
            {
                profile.id = "easy";
                profile.displayName = "Easy";
                profile.decisionIntervalSeconds = 2.5f;
                profile.workerCount = 2;
                profile.productionRateMultiplier = 0.75f;
                profile.armySize = 3;
                profile.attackCooldownSeconds = 35f;
                profile.threatRadius = 16f;
                profile.recoverHealthFraction = 0.35f;
            });

            DifficultyProfile normal = CreateOrLoad<DifficultyProfile>("Difficulty_Normal", profile =>
            {
                profile.id = "normal";
                profile.displayName = "Normal";
                profile.decisionIntervalSeconds = 2f;
                profile.workerCount = 3;
                profile.productionRateMultiplier = 1f;
                profile.armySize = 4;
                profile.attackCooldownSeconds = 25f;
                profile.threatRadius = 18f;
                profile.recoverHealthFraction = 0.3f;
            });

            DifficultyProfile hard = CreateOrLoad<DifficultyProfile>("Difficulty_Hard", profile =>
            {
                profile.id = "hard";
                profile.displayName = "Hard";
                profile.decisionIntervalSeconds = 1.5f;
                profile.workerCount = 4;
                profile.productionRateMultiplier = 1.35f;
                profile.armySize = 6;
                profile.attackCooldownSeconds = 18f;
                profile.threatRadius = 22f;
                profile.recoverHealthFraction = 0.25f;
            });

            UnitDefinition worker = CreateOrLoad<UnitDefinition>("Unit_Worker", unit =>
            {
                unit.id = "worker";
                unit.displayName = "Worker";
                unit.maxHealth = 40;
                unit.damage = 0;
                unit.attackInterval = 1f;
                unit.attackRange = 1.2f;
                unit.moveSpeed = 3.6f;
                unit.isRanged = false;
                unit.counters = new string[0];
                unit.populationCost = 1;
                unit.cost = new ResourceCost(50, 0, 0, 0);
                unit.trainSeconds = 8f;
                unit.color = new Color(0.85f, 0.78f, 0.62f);
            });

            UnitDefinition swordsman = CreateOrLoad<UnitDefinition>("Unit_Swordsman", unit =>
            {
                unit.id = "swordsman";
                unit.displayName = "Swordsman";
                unit.maxHealth = 110;
                unit.damage = 12;
                unit.attackInterval = 1f;
                unit.attackRange = 1.6f;
                unit.moveSpeed = 3.2f;
                unit.isRanged = false;
                unit.counters = new[] { "archer" };
                unit.populationCost = 2;
                unit.cost = new ResourceCost(0, 60, 20, 0);
                unit.trainSeconds = 12f;
                unit.color = new Color(0.62f, 0.66f, 0.74f);
            });

            UnitDefinition spearman = CreateOrLoad<UnitDefinition>("Unit_Spearman", unit =>
            {
                unit.id = "spearman";
                unit.displayName = "Spearman";
                unit.maxHealth = 95;
                unit.damage = 9;
                unit.attackInterval = 1.2f;
                unit.attackRange = 2.1f;
                unit.moveSpeed = 3.2f;
                unit.isRanged = false;
                unit.counters = new string[0];
                unit.populationCost = 2;
                unit.cost = new ResourceCost(30, 40, 10, 0);
                unit.trainSeconds = 11f;
                unit.color = new Color(0.7f, 0.58f, 0.42f);
            });

            BuildingDefinition townHall = CreateOrLoad<BuildingDefinition>("Building_TownHall", building =>
            {
                building.id = "town_hall";
                building.displayName = "Town Hall";
                building.maxHealth = 900;
                building.cost = ResourceCost.Zero;
                building.buildSeconds = 30f;
                building.footprint = new Vector2Int(3, 3);
                building.producesUnitId = "worker";
                building.popProvided = 10;
                building.isTownHall = true;
            });

            BuildingDefinition barracks = CreateOrLoad<BuildingDefinition>("Building_Barracks", building =>
            {
                building.id = "barracks";
                building.displayName = "Barracks";
                building.maxHealth = 450;
                building.cost = new ResourceCost(120, 0, 40, 0);
                building.buildSeconds = 20f;
                building.footprint = new Vector2Int(3, 3);
                building.producesUnitId = "swordsman";
                building.popProvided = 5;
                building.isTownHall = false;
            });

            BuildingDefinition watchtower = CreateOrLoad<BuildingDefinition>("Building_Watchtower", building =>
            {
                building.id = "watchtower";
                building.displayName = "Watchtower";
                building.maxHealth = 320;
                building.cost = new ResourceCost(80, 0, 60, 0);
                building.buildSeconds = 15f;
                building.footprint = new Vector2Int(2, 2);
                building.producesUnitId = string.Empty;
                building.popProvided = 0;
                building.isTownHall = false;
            });

            MissionDefinition mission01 = CreateOrLoad<MissionDefinition>("Mission_01_FirstSettlement", mission =>
            {
                mission.missionId = "mission_01";
                mission.title = "First Settlement";
                mission.briefing = "Claim the valley. Raise a town hall, put your workers on wood and food, and raze the enemy camp before it grows.";
                mission.terrainSeed = 20261011;
                mission.startingWood = 200;
                mission.startingFood = 150;
                mission.startingGold = 50;
                mission.enemyUnitIds = new[] { "worker", "swordsman", "swordsman" };
                mission.enemyStartDelaySeconds = 25f;
                mission.difficulty = normal;
                mission.victoryCondition = ObjectiveCondition.DestroyEnemyTownHall;
                mission.defeatCondition = ObjectiveCondition.LosePlayerTownHall;
                mission.crownReward = 2;
                mission.unlockRequirement = string.Empty;
                mission.starObjectives = new[]
                {
                    new StarObjective { description = "Destroy the enemy town hall", condition = ObjectiveCondition.DestroyEnemyTownHall, threshold = 1, crownValue = 1 },
                    new StarObjective { description = "Train 8 units", condition = ObjectiveCondition.TrainUnits, threshold = 8, crownValue = 1 },
                    new StarObjective { description = "Stockpile 250 gold", condition = ObjectiveCondition.StockpileResource, threshold = 250, resource = ResourceType.Gold, crownValue = 1 }
                };
            });

            MissionDefinition mission02 = CreateOrLoad<MissionDefinition>("Mission_02_EnemyCamp", mission =>
            {
                mission.missionId = "mission_02";
                mission.title = "The Enemy Camp";
                mission.briefing = "The camp has grown a spear line. Break it, and destroy two of their structures to make the valley yours.";
                mission.terrainSeed = 20261012;
                mission.startingWood = 250;
                mission.startingFood = 180;
                mission.startingGold = 70;
                mission.enemyUnitIds = new[] { "worker", "worker", "swordsman", "swordsman", "spearman", "spearman" };
                mission.enemyStartDelaySeconds = 20f;
                mission.difficulty = hard;
                mission.victoryCondition = ObjectiveCondition.DestroyEnemyTownHall;
                mission.defeatCondition = ObjectiveCondition.LosePlayerTownHall;
                mission.crownReward = 3;
                mission.unlockRequirement = "mission_01";
                mission.starObjectives = new[]
                {
                    new StarObjective { description = "Destroy the enemy town hall", condition = ObjectiveCondition.DestroyEnemyTownHall, threshold = 1, crownValue = 1 },
                    new StarObjective { description = "Destroy 2 enemy structures", condition = ObjectiveCondition.DestroyEnemyBuildings, threshold = 2, crownValue = 1 },
                    new StarObjective { description = "Train 12 units", condition = ObjectiveCondition.TrainUnits, threshold = 12, crownValue = 1 }
                };
            });

            database.units = new List<UnitDefinition> { worker, swordsman, spearman };
            database.buildings = new List<BuildingDefinition> { townHall, barracks, watchtower };
            database.missions = new List<MissionDefinition> { mission01, mission02 };
            database.InvalidateCaches();

            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();

            Debug.Log($"ProjectBootstrap: content assets ready — {database.units.Count} units, {database.buildings.Count} buildings, {database.missions.Count} missions, difficulty profiles easy/normal/hard.");
            return database;
        }

        /// <summary>Load an existing asset or create it, then apply the numbers and mark it dirty.</summary>
        private static T CreateOrLoad<T>(string fileName, Action<T> configure) where T : ScriptableObject
        {
            string path = $"{DataFolder}/{fileName}.asset";
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                asset.name = fileName;
                AssetDatabase.CreateAsset(asset, path);
            }

            configure(asset);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        // -----------------------------------------------------------------------------------------
        // Scenes
        // -----------------------------------------------------------------------------------------

        private static void BuildScenes(GameDatabase database)
        {
            BuildBattleScene(database);
            BuildBootstrapScene(database);
        }

        /// <summary>
        /// Re-resolve the database asset from disk. <see cref="EditorSceneManager.NewScene"/> unloads
        /// assets the outgoing scene did not reference, so a database instance captured before a
        /// scene swap is a dead stub afterwards and every definition resolved through it comes back
        /// null. Each scene builder therefore reloads it once the new scene is active.
        /// </summary>
        private static GameDatabase ReloadDatabase()
        {
            GameDatabase database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath);
            if (database != null) database.InvalidateCaches();
            return database;
        }

        /// <summary>The first mission, which is what the authored battle scene plays.</summary>
        private static MissionDefinition FirstMission(GameDatabase database)
        {
            if (database == null || database.missions == null || database.missions.Count == 0) return null;
            return database.missions[0];
        }

        private static void BuildBattleScene(GameDatabase database)
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // NewScene unloads assets the outgoing scene did not reference, which invalidates any
            // database instance captured before the swap. Re-resolve it from disk before use.
            GameDatabase liveDatabase = ReloadDatabase() ?? database;
            MissionDefinition mission = FirstMission(liveDatabase);

            Material grass = CreateMaterialAsset("Ground_Grass", GrassColor);
            Material bark = CreateMaterialAsset("Tree_Bark", BarkColor);
            Material leaf = CreateMaterialAsset("Tree_Leaf", LeafColor);
            Material farm = CreateMaterialAsset("Farm_Soil", FarmColor);
            Material gold = CreateMaterialAsset("Gold_Vein", GoldColor);
            Material playerBuilding = CreateMaterialAsset("Building_Player", BuildingColor);
            Material enemyBuilding = CreateMaterialAsset("Building_Enemy", EnemyBuildingColor);

            UnitDefinition workerDefinition = liveDatabase.FindUnit("worker");
            BuildingDefinition townHallDefinition = liveDatabase.FindBuilding("town_hall");
            BuildingDefinition barracksDefinition = liveDatabase.FindBuilding("barracks");
            UnitDefinition swordsmanDefinition = liveDatabase.FindUnit("swordsman");
            UnitDefinition spearmanDefinition = liveDatabase.FindUnit("spearman");

            Material workerMaterial = CreateDefinitionMaterial(workerDefinition);
            Material swordsmanMaterial = CreateDefinitionMaterial(swordsmanDefinition);
            Material spearmanMaterial = CreateDefinitionMaterial(spearmanDefinition);

            CreateDirectionalLight();

            // Ground: a 64x64 plane matching the navigation grid that BattleManager builds at the origin.
            GameObject ground = CreatePrimitive("Ground", PrimitiveType.Plane, new Vector3(32f, 0f, 32f), new Vector3(6.4f, 1f, 6.4f), grass);
            ground.isStatic = true;

            // Camera rig, with the camera as a child so the rig pivot is the point under the screen centre.
            var rigGo = new GameObject("CameraRig");
            rigGo.transform.position = new Vector3(32f, 0f, 32f);
            IsometricCameraRig rig = rigGo.AddComponent<IsometricCameraRig>();
            rig.battlefieldBounds = new Bounds(new Vector3(32f, 0f, 32f), new Vector3(64f, 0f, 64f));

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.SetParent(rigGo.transform, false);
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.backgroundColor = new Color(0.44f, 0.55f, 0.65f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            rig.ApplyConfiguration();

            // Systems.
            var bootstrapGo = new GameObject("GameBootstrap");
            GameBootstrap bootstrap = bootstrapGo.AddComponent<GameBootstrap>();
            bootstrap.loadBattleSceneOnStart = false;
            bootstrap.database = liveDatabase;
            bootstrap.mission = mission;
            bootstrap.economyBalance = liveDatabase.economy;

            var managerGo = new GameObject("BattleManager");
            BattleManager battleManager = managerGo.AddComponent<BattleManager>();
            battleManager.gridWidth = 64;
            battleManager.gridHeight = 64;
            battleManager.cellSize = 1f;
            battleManager.spawnMissionEnemies = true;

            var pathfindingGo = new GameObject("PathfindingManager");
            PathfindingManager pathfinding = pathfindingGo.AddComponent<PathfindingManager>();
            pathfinding.width = 64;
            pathfinding.height = 64;
            pathfinding.cellSize = 1f;

            var poolGo = new GameObject("ProjectilePool");
            ProjectilePool pool = poolGo.AddComponent<ProjectilePool>();
            pool.prewarmCount = 64;
            pool.maxSize = 256;

            var systemsGo = new GameObject("Systems");
            UnitSelectionManager selection = systemsGo.AddComponent<UnitSelectionManager>();
            selection.SelectionCamera = camera;
            BuildingPlacementSystem placement = systemsGo.AddComponent<BuildingPlacementSystem>();
            BattleInputController input = systemsGo.AddComponent<BattleInputController>();
            EnemyAIController enemyAI = systemsGo.AddComponent<EnemyAIController>();
            enemyAI.trainableUnitIds = new[] { "swordsman", "spearman" };
            enemyAI.workerUnitId = "worker";
            enemyAI.defensiveBuildingId = "watchtower";

            bootstrap.battleManager = battleManager;
            bootstrap.pathfinding = pathfinding;
            bootstrap.projectilePool = pool;
            bootstrap.selection = selection;
            bootstrap.cameraRig = rig;
            bootstrap.placement = placement;
            bootstrap.input = input;

            battleManager.pathfinding = pathfinding;
            battleManager.projectilePool = pool;
            battleManager.enemyAI = enemyAI;
            battleManager.cameraRig = rig;

            input.selection = selection;
            input.cameraRig = rig;
            input.placement = placement;

            // Player holdings.
            CreateBuilding("PlayerTownHall", new Vector3(9f, 0f, 9f), townHallDefinition, Team.Player, playerBuilding, 3.4f);
            CreateUnit("Worker_1", new Vector3(6f, 0f, 12f), workerDefinition, Team.Player, workerMaterial);
            CreateUnit("Worker_2", new Vector3(12f, 0f, 6f), workerDefinition, Team.Player, workerMaterial);
            CreateUnit("Worker_3", new Vector3(13f, 0f, 13f), workerDefinition, Team.Player, workerMaterial);

            // Resource field around the player's start.
            CreateTree("Tree_1", new Vector3(4f, 0f, 8f), bark, leaf, 120);
            CreateTree("Tree_2", new Vector3(5f, 0f, 12f), bark, leaf, 120);
            CreateTree("Tree_3", new Vector3(15f, 0f, 10f), bark, leaf, 120);
            CreateResourceNode("Farm_1", PrimitiveType.Cube, new Vector3(16f, 0.15f, 5f), new Vector3(3.2f, 0.3f, 3.2f), farm, ResourceType.Food, 180);
            CreateResourceNode("Farm_2", PrimitiveType.Cube, new Vector3(6f, 0.15f, 16f), new Vector3(3.2f, 0.3f, 3.2f), farm, ResourceType.Food, 180);
            CreateResourceNode("Gold_1", PrimitiveType.Sphere, new Vector3(18f, 0.7f, 16f), new Vector3(1.6f, 1.4f, 1.6f), gold, ResourceType.Gold, 90);

            // Enemy camp, in the opposite corner of the grid.
            CreateBuilding("EnemyTownHall", new Vector3(54f, 0f, 54f), townHallDefinition, Team.Enemy, enemyBuilding, 3.4f);
            CreateBuilding("EnemyBarracks", new Vector3(48f, 0f, 52f), barracksDefinition, Team.Enemy, enemyBuilding, 3.1f);
            CreateUnit("EnemySwordsman_1", new Vector3(51f, 0f, 50f), swordsmanDefinition, Team.Enemy, swordsmanMaterial);
            CreateUnit("EnemySpearman_1", new Vector3(53f, 0f, 49f), spearmanDefinition, Team.Enemy, spearmanMaterial);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, BattleScenePath))
            {
                throw new InvalidOperationException($"Failed to save the battle scene to '{BattleScenePath}'.");
            }

            Debug.Log($"ProjectBootstrap: battle scene built at '{BattleScenePath}'.");
        }

        private static void BuildBootstrapScene(GameDatabase database)
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Same reload rule as the battle scene: the instance passed in did not survive the swap.
            GameDatabase liveDatabase = ReloadDatabase() ?? database;

            var go = new GameObject("GameBootstrap");
            GameBootstrap bootstrap = go.AddComponent<GameBootstrap>();
            bootstrap.loadBattleSceneOnStart = true;
            bootstrap.battleSceneName = "Battle";
            bootstrap.database = liveDatabase;
            bootstrap.economyBalance = liveDatabase.economy;

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, BootstrapScenePath))
            {
                throw new InvalidOperationException($"Failed to save the bootstrap scene to '{BootstrapScenePath}'.");
            }

            Debug.Log($"ProjectBootstrap: bootstrap scene built at '{BootstrapScenePath}'.");
        }

        private static void ConfigureBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootstrapScenePath, true),
                new EditorBuildSettingsScene(BattleScenePath, true)
            };

            Debug.Log($"ProjectBootstrap: build settings hold {EditorBuildSettings.scenes.Length} scenes (Bootstrap first).");
        }

        // -----------------------------------------------------------------------------------------
        // Scene object helpers
        // -----------------------------------------------------------------------------------------

        private static void CreateDirectionalLight()
        {
            var go = new GameObject("Directional Light");
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.89f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
        }

        private static GameObject CreatePrimitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;

            Collider collider = go.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);

            go.transform.position = position;
            go.transform.localScale = scale;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;

            return go;
        }

        private static void CreateUnit(string name, Vector3 position, UnitDefinition definition, Team team, Material material)
        {
            if (definition == null) return;

            var go = new GameObject(name);
            go.transform.position = position;

            var body = GameObject.CreatePrimitive(definition.IsCombatUnit ? PrimitiveType.Capsule : PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = definition.IsCombatUnit ? new Vector3(0.7f, 0.75f, 0.7f) : new Vector3(0.65f, 0.5f, 0.65f);
            body.transform.localPosition = new Vector3(0f, definition.IsCombatUnit ? 0.75f : 0.5f, 0f);

            var renderer = body.GetComponent<MeshRenderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;

            UnitController unit = go.AddComponent<UnitController>();
            unit.team = team;
            unit.definition = definition;
        }

        /// <summary>A tree is a harvestable trunk plus a non-harvestable canopy child.</summary>
        private static void CreateTree(string name, Vector3 basePosition, Material bark, Material leaf, int capacity)
        {
            GameObject trunk = CreatePrimitive(name, PrimitiveType.Cylinder, basePosition + new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), bark);

            ResourceNode node = trunk.AddComponent<ResourceNode>();
            node.Configure(ResourceType.Wood, capacity);

            GameObject canopy = CreatePrimitive($"{name}_Canopy", PrimitiveType.Sphere, basePosition + new Vector3(0f, 2.1f, 0f), new Vector3(2.4f, 1.6f, 2.4f), leaf);
            canopy.transform.SetParent(trunk.transform, true);
        }

        /// <summary>One material asset per unit definition, coloured from the definition itself.</summary>
        private static Material CreateDefinitionMaterial(UnitDefinition definition)
        {
            if (definition == null) return null;
            return CreateMaterialAsset($"Unit_{definition.id}", definition.color);
        }

        private static void CreateBuilding(string name, Vector3 position, BuildingDefinition definition, Team team, Material material, float height)
        {
            if (definition == null) return;

            var go = new GameObject(name);
            go.transform.position = position;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = new Vector3(definition.footprint.x * 0.95f, height, definition.footprint.y * 0.95f);
            body.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);

            var renderer = body.GetComponent<MeshRenderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;

            BuildingController building = go.AddComponent<BuildingController>();
            building.team = team;
            building.definition = definition;
            building.completedColor = material != null ? MaterialBaseColor(material) : Color.gray;
        }

        /// <summary>A single harvestable deposit: a farm plot or a gold vein.</summary>
        private static void CreateResourceNode(
            string name,
            PrimitiveType type,
            Vector3 position,
            Vector3 scale,
            Material material,
            ResourceType resource,
            int capacity)
        {
            GameObject go = CreatePrimitive(name, type, position, scale, material);

            ResourceNode node = go.AddComponent<ResourceNode>();
            node.Configure(resource, capacity);
        }

        private static Color MaterialBaseColor(Material material)
        {
            if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
            if (material.HasProperty("_Color")) return material.GetColor("_Color");
            return Color.gray;
        }

        private static Material CreateMaterialAsset(string name, Color color)
        {
            string path = $"{ArtFolder}/{name}.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException("Shader 'Universal Render Pipeline/Lit' was not found — is the URP package resolved?");
            }

            var material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}

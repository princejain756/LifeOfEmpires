using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using LifeOfEmpires.Audio;
using LifeOfEmpires.UI;

namespace LifeOfEmpires.Editor
{
    /// <summary>
    /// Creates every generated asset this project needs: the URP pipeline assets, the Mobile quality
    /// tier, the iOS player settings, the ScriptableObject balance/content assets, and the two scenes.
    ///
    /// It exists because none of this can be hand-authored reliably. ProjectSettings YAML and URP
    /// asset GUID wiring are exactly the things that silently break a project, and the engine is the
    /// only authority on their shape.
    ///
    /// Run it from the menu (<c>LifeOfEmpires/Bootstrap Project</c>) or headlessly:
    /// <c>Unity -batchmode -quit -executeMethod LifeOfEmpires.Editor.ProjectBootstrap.Run</c>
    /// </summary>
    public static class ProjectBootstrap
    {
        private const string Root = "Assets/LifeOfEmpires";
        private const string SettingsFolder = Root + "/Settings";
        private const string DataFolder = Root + "/Data";
        private const string ScenesFolder = Root + "/Scenes";
        private const string ArtFolder = Root + "/Art/Materials";
        private const string AudioFolder = Root + "/Audio";

        private const string UrpAssetPath = SettingsFolder + "/LifeOfEmpires_URP.asset";
        private const string UrpRendererPath = SettingsFolder + "/LifeOfEmpires_Renderer.asset";
        private const string DatabasePath = DataFolder + "/GameDatabase.asset";
        private const string BattleScenePath = ScenesFolder + "/Battle.unity";
        private const string BootstrapScenePath = ScenesFolder + "/Bootstrap.unity";
        private const string CampaignMapScenePath = ScenesFolder + "/CampaignMap.unity";
        private const string SkirmishScenePath = ScenesFolder + "/Skirmish.unity";
        private const string KingdomScenePath = ScenesFolder + "/Kingdom.unity";
        private const string MobileQualityName = "Mobile";

        /// <summary>Spec palette: the battlefield ground colour.</summary>
        private static readonly Color GrassColor = new Color(133f / 255f, 166f / 255f, 106f / 255f);
        private static readonly Color BarkColor = new Color(0.36f, 0.26f, 0.17f);
        private static readonly Color LeafColor = new Color(0.24f, 0.45f, 0.22f);
        private static readonly Color FarmColor = new Color(0.79f, 0.68f, 0.29f);
        private static readonly Color GoldColor = new Color(0.85f, 0.71f, 0.22f);
        private static readonly Color BuildingColor = new Color(0.72f, 0.66f, 0.55f);
        private static readonly Color EnemyBuildingColor = new Color(0.58f, 0.42f, 0.40f);

        [MenuItem("LifeOfEmpires/Bootstrap Project")]
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
                EnsureShaderKeepMaterials();
                EnsureUiSprites();
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

        // Runtime code resolves shaders by name with Shader.Find, but nothing in the build keeps a
        // shader alive unless a shipped asset references it. One material per shader in Resources
        // is the smallest durable reference; without it every runtime material renders magenta.
        private static void EnsureShaderKeepMaterials()
        {
            EnsureFolder(Root, "Resources");
            string resourcesFolder = Root + "/Resources";
            foreach (string shaderName in ShaderKeepNames)
            {
                string path = $"{resourcesFolder}/ShaderKeep_{shaderName.Replace('/', '_').Replace(' ', '_')}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) continue;
                Shader shader = Shader.Find(shaderName);
                if (shader == null) continue; // fallback shader absent on this install — harmless
                AssetDatabase.CreateAsset(new Material(shader), path);
            }
        }

        private static readonly string[] ShaderKeepNames =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
            "Sprites/Default",
            "Standard"
        };

        // Generated UI art lands as plain PNGs in Resources/UI; without this pass they import as
        // Default textures and every Resources.Load<Sprite> the HUD does returns null. tex_grass
        // is the one exception — it feeds a 3D material, not a sprite.
        private static void EnsureUiSprites()
        {
            string uiFolder = Root + "/Resources/UI";
            if (!AssetDatabase.IsValidFolder(uiFolder)) return;

            AssetDatabase.Refresh();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { uiFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;

                if (path.EndsWith("/tex_grass.png", StringComparison.Ordinal))
                {
                    if (importer.textureType != TextureImporterType.Default || !importer.mipmapEnabled)
                    {
                        importer.textureType = TextureImporterType.Default;
                        importer.mipmapEnabled = true;
                        importer.SaveAndReimport();
                    }
                    continue;
                }

                if (importer.textureType == TextureImporterType.Sprite
                    && importer.spriteImportMode == SpriteImportMode.Single
                    && importer.alphaIsTransparency) continue;

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
        }

        // FBX-embedded materials import on whatever shader the importer picks. Instances get
        // re-slotted onto shared URP Lit materials at spawn time inside ModelLibrary, so the
        // palette lives there (runtime-safe) rather than in this editor-only pass.

        // -----------------------------------------------------------------------------------------
        // Render pipeline
        // -----------------------------------------------------------------------------------------

        private static RenderPipelineAsset CreateRenderPipelineAssets()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UrpRendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                rendererData.name = "LifeOfEmpires_Renderer";
                AssetDatabase.CreateAsset(rendererData, UrpRendererPath);
            }

            var pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (pipelineAsset == null)
            {
                pipelineAsset = UniversalRenderPipelineAsset.Create(rendererData);
                pipelineAsset.name = "LifeOfEmpires_URP";
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
            PlayerSettings.companyName = "Life of Empires";
            PlayerSettings.productName = "Life of Empires";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.princejain756.lifeofempires");
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

            UnitDefinition archer = CreateOrLoad<UnitDefinition>("Unit_Archer", unit =>
            {
                unit.id = "archer";
                unit.displayName = "Archer";
                unit.maxHealth = 70;
                unit.damage = 11;
                unit.attackInterval = 1.4f;
                unit.attackRange = 9f;
                unit.moveSpeed = 3.4f;
                unit.isRanged = true;
                unit.counters = new string[0];
                unit.populationCost = 1;
                unit.cost = new ResourceCost(40, 30, 10, 0);
                unit.trainSeconds = 14f;
                unit.color = new Color(0.45f, 0.62f, 0.38f);
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

            MissionDefinition mission03 = CreateOrLoad<MissionDefinition>("Mission_03_Raiders", mission =>
            {
                mission.missionId = "mission_03";
                mission.title = "Greenwood Valley: Raiders";
                mission.briefing = "The Greenwood Valley raiders have returned. Train a handful of soldiers and take their camp.";
                mission.terrainSeed = 20261013;
                mission.startingWood = 250;
                mission.startingFood = 180;
                mission.startingGold = 60;
                mission.enemyUnitIds = new[] { "worker", "swordsman", "spearman" };
                mission.enemyStartDelaySeconds = 18f;
                mission.difficulty = normal;
                mission.victoryCondition = ObjectiveCondition.DestroyEnemyTownHall;
                mission.defeatCondition = ObjectiveCondition.LosePlayerTownHall;
                mission.crownReward = 3;
                mission.unlockRequirement = "mission_02";
                mission.starObjectives = new[]
                {
                    new StarObjective { description = "Destroy the enemy town hall", condition = ObjectiveCondition.DestroyEnemyTownHall, threshold = 1, crownValue = 1 },
                    new StarObjective { description = "Train 10 units", condition = ObjectiveCondition.TrainUnits, threshold = 10, crownValue = 1 },
                    new StarObjective { description = "Stockpile 200 gold", condition = ObjectiveCondition.StockpileResource, threshold = 200, resource = ResourceType.Gold, crownValue = 1 }
                };
            });

            MissionDefinition mission04 = CreateOrLoad<MissionDefinition>("Mission_04_StonebridgeWatch", mission =>
            {
                mission.missionId = "mission_04";
                mission.title = "Stonebridge: The Watch";
                mission.briefing = "Stonebridge is watched from a guard post. Break the watch and hold the crossing.";
                mission.terrainSeed = 20261014;
                mission.startingWood = 260;
                mission.startingFood = 190;
                mission.startingGold = 70;
                mission.enemyUnitIds = new[] { "worker", "worker", "swordsman", "spearman", "archer" };
                mission.enemyStartDelaySeconds = 16f;
                mission.difficulty = normal;
                mission.victoryCondition = ObjectiveCondition.DestroyEnemyTownHall;
                mission.defeatCondition = ObjectiveCondition.LosePlayerTownHall;
                mission.crownReward = 3;
                mission.unlockRequirement = "mission_03";
                mission.starObjectives = new[]
                {
                    new StarObjective { description = "Destroy the enemy town hall", condition = ObjectiveCondition.DestroyEnemyTownHall, threshold = 1, crownValue = 1 },
                    new StarObjective { description = "Destroy 2 enemy structures", condition = ObjectiveCondition.DestroyEnemyBuildings, threshold = 2, crownValue = 1 },
                    new StarObjective { description = "Stockpile 250 gold", condition = ObjectiveCondition.StockpileResource, threshold = 250, resource = ResourceType.Gold, crownValue = 1 }
                };
            });

            MissionDefinition mission05 = CreateOrLoad<MissionDefinition>("Mission_05_FirstRecruits", mission =>
            {
                mission.missionId = "mission_05";
                mission.title = "Stonebridge: First Recruits";
                mission.briefing = "Stonebridge is yours, but the recruit line is thin. Raise a bigger army before you push on.";
                mission.terrainSeed = 20261015;
                mission.startingWood = 280;
                mission.startingFood = 200;
                mission.startingGold = 70;
                mission.enemyUnitIds = new[] { "worker", "worker", "swordsman", "spearman", "archer", "archer" };
                mission.enemyStartDelaySeconds = 15f;
                mission.difficulty = normal;
                mission.victoryCondition = ObjectiveCondition.DestroyEnemyTownHall;
                mission.defeatCondition = ObjectiveCondition.LosePlayerTownHall;
                mission.crownReward = 4;
                mission.unlockRequirement = "mission_04";
                mission.starObjectives = new[]
                {
                    new StarObjective { description = "Destroy the enemy town hall", condition = ObjectiveCondition.DestroyEnemyTownHall, threshold = 1, crownValue = 1 },
                    new StarObjective { description = "Train 14 units", condition = ObjectiveCondition.TrainUnits, threshold = 14, crownValue = 1 },
                    new StarObjective { description = "Stockpile 300 gold", condition = ObjectiveCondition.StockpileResource, threshold = 300, resource = ResourceType.Gold, crownValue = 1 }
                };
            });

            MissionDefinition mission06 = CreateOrLoad<MissionDefinition>("Mission_06_IronHillsGarrison", mission =>
            {
                mission.missionId = "mission_06";
                mission.title = "The Iron Hills: Garrison";
                mission.briefing = "The Iron Hills are held by a garrison in depth. Bring ranged support to crack it.";
                mission.terrainSeed = 20261016;
                mission.startingWood = 290;
                mission.startingFood = 210;
                mission.startingGold = 80;
                mission.enemyUnitIds = new[] { "worker", "worker", "spearman", "spearman", "archer" };
                mission.enemyStartDelaySeconds = 14f;
                mission.difficulty = hard;
                mission.victoryCondition = ObjectiveCondition.DestroyEnemyTownHall;
                mission.defeatCondition = ObjectiveCondition.LosePlayerTownHall;
                mission.crownReward = 4;
                mission.unlockRequirement = "mission_05";
                mission.starObjectives = new[]
                {
                    new StarObjective { description = "Destroy the enemy town hall", condition = ObjectiveCondition.DestroyEnemyTownHall, threshold = 1, crownValue = 1 },
                    new StarObjective { description = "Destroy 3 enemy structures", condition = ObjectiveCondition.DestroyEnemyBuildings, threshold = 3, crownValue = 1 },
                    new StarObjective { description = "Stockpile 350 gold", condition = ObjectiveCondition.StockpileResource, threshold = 350, resource = ResourceType.Gold, crownValue = 1 }
                };
            });

            MissionDefinition mission07 = CreateOrLoad<MissionDefinition>("Mission_07_RangedSkirmish", mission =>
            {
                mission.missionId = "mission_07";
                mission.title = "The Iron Hills: Ranged Skirmish";
                mission.briefing = "Enemy archers rule the Iron Hills ridgeline. Answer them at range.";
                mission.terrainSeed = 20261017;
                mission.startingWood = 300;
                mission.startingFood = 220;
                mission.startingGold = 80;
                mission.enemyUnitIds = new[] { "worker", "worker", "swordsman", "spearman", "archer", "archer" };
                mission.enemyStartDelaySeconds = 12f;
                mission.difficulty = hard;
                mission.victoryCondition = ObjectiveCondition.DestroyEnemyTownHall;
                mission.defeatCondition = ObjectiveCondition.LosePlayerTownHall;
                mission.crownReward = 5;
                mission.unlockRequirement = "mission_06";
                mission.starObjectives = new[]
                {
                    new StarObjective { description = "Destroy the enemy town hall", condition = ObjectiveCondition.DestroyEnemyTownHall, threshold = 1, crownValue = 1 },
                    new StarObjective { description = "Train 16 units", condition = ObjectiveCondition.TrainUnits, threshold = 16, crownValue = 1 },
                    new StarObjective { description = "Destroy 2 enemy structures", condition = ObjectiveCondition.DestroyEnemyBuildings, threshold = 2, crownValue = 1 }
                };
            });

            MissionDefinition mission08 = CreateOrLoad<MissionDefinition>("Mission_08_SunfallOutpost", mission =>
            {
                mission.missionId = "mission_08";
                mission.title = "Sunfall Plains: Outpost";
                mission.briefing = "The Sunfall Plains outpost is fortified. Take it before it calls for help.";
                mission.terrainSeed = 20261018;
                mission.startingWood = 310;
                mission.startingFood = 230;
                mission.startingGold = 90;
                mission.enemyUnitIds = new[] { "worker", "swordsman", "spearman", "spearman", "archer", "archer" };
                mission.enemyStartDelaySeconds = 12f;
                mission.difficulty = hard;
                mission.victoryCondition = ObjectiveCondition.DestroyEnemyTownHall;
                mission.defeatCondition = ObjectiveCondition.LosePlayerTownHall;
                mission.crownReward = 5;
                mission.unlockRequirement = "mission_07";
                mission.starObjectives = new[]
                {
                    new StarObjective { description = "Destroy the enemy town hall", condition = ObjectiveCondition.DestroyEnemyTownHall, threshold = 1, crownValue = 1 },
                    new StarObjective { description = "Train 18 units", condition = ObjectiveCondition.TrainUnits, threshold = 18, crownValue = 1 },
                    new StarObjective { description = "Stockpile 400 gold", condition = ObjectiveCondition.StockpileResource, threshold = 400, resource = ResourceType.Gold, crownValue = 1 }
                };
            });

            MissionDefinition mission09 = CreateOrLoad<MissionDefinition>("Mission_09_SunfallSiege", mission =>
            {
                mission.missionId = "mission_09";
                mission.title = "Sunfall Plains: The Siege";
                mission.briefing = "The Sunfall siege begins. Your archers and spears must break a defended town.";
                mission.terrainSeed = 20261019;
                mission.startingWood = 330;
                mission.startingFood = 240;
                mission.startingGold = 100;
                mission.enemyUnitIds = new[] { "worker", "worker", "spearman", "spearman", "archer", "archer", "archer" };
                mission.enemyStartDelaySeconds = 10f;
                mission.difficulty = hard;
                mission.victoryCondition = ObjectiveCondition.DestroyEnemyTownHall;
                mission.defeatCondition = ObjectiveCondition.LosePlayerTownHall;
                mission.crownReward = 6;
                mission.unlockRequirement = "mission_08";
                mission.starObjectives = new[]
                {
                    new StarObjective { description = "Destroy the enemy town hall", condition = ObjectiveCondition.DestroyEnemyTownHall, threshold = 1, crownValue = 1 },
                    new StarObjective { description = "Destroy 4 enemy structures", condition = ObjectiveCondition.DestroyEnemyBuildings, threshold = 4, crownValue = 1 },
                    new StarObjective { description = "Stockpile 500 gold", condition = ObjectiveCondition.StockpileResource, threshold = 500, resource = ResourceType.Gold, crownValue = 1 }
                };
            });

            MissionDefinition mission10 = CreateOrLoad<MissionDefinition>("Mission_10_AshenFortress", mission =>
            {
                mission.missionId = "mission_10";
                mission.title = "Ashen Frontier: The Fortress";
                mission.briefing = "The Ashen Frontier fortress bristles with archers. Bring siege numbers or go home.";
                mission.terrainSeed = 20261020;
                mission.startingWood = 340;
                mission.startingFood = 250;
                mission.startingGold = 110;
                mission.enemyUnitIds = new[] { "spearman", "spearman", "swordsman", "archer", "archer", "archer" };
                mission.enemyStartDelaySeconds = 10f;
                mission.difficulty = hard;
                mission.victoryCondition = ObjectiveCondition.DestroyEnemyTownHall;
                mission.defeatCondition = ObjectiveCondition.LosePlayerTownHall;
                mission.crownReward = 7;
                mission.unlockRequirement = "mission_09";
                mission.starObjectives = new[]
                {
                    new StarObjective { description = "Destroy the enemy town hall", condition = ObjectiveCondition.DestroyEnemyTownHall, threshold = 1, crownValue = 1 },
                    new StarObjective { description = "Train 20 units", condition = ObjectiveCondition.TrainUnits, threshold = 20, crownValue = 1 },
                    new StarObjective { description = "Stockpile 550 gold", condition = ObjectiveCondition.StockpileResource, threshold = 550, resource = ResourceType.Gold, crownValue = 1 }
                };
            });

            MissionDefinition mission11 = CreateOrLoad<MissionDefinition>("Mission_11_CrownlandsGate", mission =>
            {
                mission.missionId = "mission_11";
                mission.title = "The Crownlands: The Gate";
                mission.briefing = "The Crownlands' gate is the last wall between you and the capital. Storm it.";
                mission.terrainSeed = 20261021;
                mission.startingWood = 360;
                mission.startingFood = 260;
                mission.startingGold = 120;
                mission.enemyUnitIds = new[] { "spearman", "swordsman", "spearman", "archer", "archer", "archer", "archer" };
                mission.enemyStartDelaySeconds = 8f;
                mission.difficulty = hard;
                mission.victoryCondition = ObjectiveCondition.DestroyEnemyTownHall;
                mission.defeatCondition = ObjectiveCondition.LosePlayerTownHall;
                mission.crownReward = 8;
                mission.unlockRequirement = "mission_10";
                mission.starObjectives = new[]
                {
                    new StarObjective { description = "Destroy the enemy town hall", condition = ObjectiveCondition.DestroyEnemyTownHall, threshold = 1, crownValue = 1 },
                    new StarObjective { description = "Destroy 5 enemy structures", condition = ObjectiveCondition.DestroyEnemyBuildings, threshold = 5, crownValue = 1 },
                    new StarObjective { description = "Stockpile 600 gold", condition = ObjectiveCondition.StockpileResource, threshold = 600, resource = ResourceType.Gold, crownValue = 1 }
                };
            });

            MissionDefinition mission12 = CreateOrLoad<MissionDefinition>("Mission_12_CapitalOfCrownlands", mission =>
            {
                mission.missionId = "mission_12";
                mission.title = "The Crownlands: Capital";
                mission.briefing = "The Crownlands' capital stands between you and a unified continent. End it.";
                mission.terrainSeed = 20261022;
                mission.startingWood = 400;
                mission.startingFood = 300;
                mission.startingGold = 150;
                mission.enemyUnitIds = new[] { "swordsman", "spearman", "spearman", "archer", "archer", "swordsman", "swordsman" };
                mission.enemyStartDelaySeconds = 8f;
                mission.difficulty = hard;
                mission.victoryCondition = ObjectiveCondition.DestroyEnemyTownHall;
                mission.defeatCondition = ObjectiveCondition.LosePlayerTownHall;
                mission.crownReward = 10;
                mission.unlockRequirement = "mission_11";
                mission.starObjectives = new[]
                {
                    new StarObjective { description = "Destroy the enemy town hall", condition = ObjectiveCondition.DestroyEnemyTownHall, threshold = 1, crownValue = 1 },
                    new StarObjective { description = "Destroy 6 enemy structures", condition = ObjectiveCondition.DestroyEnemyBuildings, threshold = 6, crownValue = 1 },
                    new StarObjective { description = "Stockpile 700 gold", condition = ObjectiveCondition.StockpileResource, threshold = 700, resource = ResourceType.Gold, crownValue = 1 }
                };
            });

            MissionDefinition skirmishForest = CreateOrLoad<MissionDefinition>("Skirmish_01_Greenwood", mission =>
            {
                mission.missionId = "skirmish_01";
                mission.title = "Skirmish: Greenwood";
                mission.briefing = "A quick-play battle on the Greenwood forest map. Skirmishes never count against the campaign.";
                mission.terrainSeed = 20261101;
                mission.startingWood = 300;
                mission.startingFood = 250;
                mission.startingGold = 100;
                mission.enemyUnitIds = new[] { "worker", "swordsman", "spearman" };
                mission.enemyStartDelaySeconds = 18f;
                mission.difficulty = easy;
                mission.victoryCondition = ObjectiveCondition.DestroyEnemyTownHall;
                mission.defeatCondition = ObjectiveCondition.LosePlayerTownHall;
                mission.crownReward = 1;
                mission.isSkirmish = true;
                mission.unlockRequirement = string.Empty;
                mission.starObjectives = new[]
                {
                    new StarObjective { description = "Destroy the enemy town hall", condition = ObjectiveCondition.DestroyEnemyTownHall, threshold = 1, crownValue = 1 },
                    new StarObjective { description = "Train 10 units", condition = ObjectiveCondition.TrainUnits, threshold = 10, crownValue = 1 }
                };
            });

            MissionDefinition skirmishPlains = CreateOrLoad<MissionDefinition>("Skirmish_02_Stonebridge", mission =>
            {
                mission.missionId = "skirmish_02";
                mission.title = "Skirmish: Stonebridge";
                mission.briefing = "A quick-play battle across the Stonebridge plains. Skirmishes never count against the campaign.";
                mission.terrainSeed = 20261102;
                mission.startingWood = 300;
                mission.startingFood = 250;
                mission.startingGold = 100;
                mission.enemyUnitIds = new[] { "worker", "swordsman", "spearman", "archer" };
                mission.enemyStartDelaySeconds = 16f;
                mission.difficulty = normal;
                mission.victoryCondition = ObjectiveCondition.DestroyEnemyTownHall;
                mission.defeatCondition = ObjectiveCondition.LosePlayerTownHall;
                mission.crownReward = 2;
                mission.isSkirmish = true;
                mission.unlockRequirement = string.Empty;
                mission.starObjectives = new[]
                {
                    new StarObjective { description = "Destroy the enemy town hall", condition = ObjectiveCondition.DestroyEnemyTownHall, threshold = 1, crownValue = 1 },
                    new StarObjective { description = "Train 12 units", condition = ObjectiveCondition.TrainUnits, threshold = 12, crownValue = 1 }
                };
            });

            MissionDefinition skirmishHighlands = CreateOrLoad<MissionDefinition>("Skirmish_03_IronHills", mission =>
            {
                mission.missionId = "skirmish_03";
                mission.title = "Skirmish: Iron Hills";
                mission.briefing = "A quick-play battle in the Iron Hills highlands. Skirmishes never count against the campaign.";
                mission.terrainSeed = 20261103;
                mission.startingWood = 300;
                mission.startingFood = 250;
                mission.startingGold = 100;
                mission.enemyUnitIds = new[] { "spearman", "spearman", "archer", "archer", "swordsman" };
                mission.enemyStartDelaySeconds = 14f;
                mission.difficulty = hard;
                mission.victoryCondition = ObjectiveCondition.DestroyEnemyTownHall;
                mission.defeatCondition = ObjectiveCondition.LosePlayerTownHall;
                mission.crownReward = 3;
                mission.isSkirmish = true;
                mission.unlockRequirement = string.Empty;
                mission.starObjectives = new[]
                {
                    new StarObjective { description = "Destroy the enemy town hall", condition = ObjectiveCondition.DestroyEnemyTownHall, threshold = 1, crownValue = 1 },
                    new StarObjective { description = "Train 14 units", condition = ObjectiveCondition.TrainUnits, threshold = 14, crownValue = 1 }
                };
            });

            KingdomUpgradeDefinition upgradeVillage = CreateOrLoad<KingdomUpgradeDefinition>("Upgrade_Village", upgrade =>
            {
                upgrade.upgradeId = "upg_village";
                upgrade.title = "Growing Village";
                upgrade.crownCost = 8;
                upgrade.bonusStartingWorkers = 0;
                upgrade.startingResourceBonus = 0f;
                upgrade.kingdomStage = 1;
                upgrade.description = "The settlement grows into a village.";
            });

            KingdomUpgradeDefinition upgradeFortified = CreateOrLoad<KingdomUpgradeDefinition>("Upgrade_Fortified", upgrade =>
            {
                upgrade.upgradeId = "upg_fortified";
                upgrade.title = "Fortified Town";
                upgrade.crownCost = 14;
                upgrade.bonusStartingWorkers = 0;
                upgrade.startingResourceBonus = 0.05f;
                upgrade.kingdomStage = 2;
                upgrade.description = "Stockades and storehouses add 5% to every opening stockpile.";
            });

            KingdomUpgradeDefinition upgradeCastle = CreateOrLoad<KingdomUpgradeDefinition>("Upgrade_Castle", upgrade =>
            {
                upgrade.upgradeId = "upg_castle";
                upgrade.title = "Stone Castle";
                upgrade.crownCost = 22;
                upgrade.bonusStartingWorkers = 0;
                upgrade.startingResourceBonus = 0.10f;
                upgrade.kingdomStage = 3;
                upgrade.description = "Stone walls and full granaries add 10% to every opening stockpile.";
            });

            KingdomUpgradeDefinition upgradeStronghold = CreateOrLoad<KingdomUpgradeDefinition>("Upgrade_Stronghold", upgrade =>
            {
                upgrade.upgradeId = "upg_stronghold";
                upgrade.title = "Royal Stronghold";
                upgrade.crownCost = 35;
                upgrade.bonusStartingWorkers = 0;
                upgrade.startingResourceBonus = 0.15f;
                upgrade.kingdomStage = 4;
                upgrade.description = "A royal stronghold adds 15% to every opening stockpile.";
            });

            KingdomUpgradeDefinition upgradeVanguard = CreateOrLoad<KingdomUpgradeDefinition>("Upgrade_Vanguard", upgrade =>
            {
                upgrade.upgradeId = "upg_vanguard";
                upgrade.title = "Vanguard Doctrine";
                upgrade.crownCost = 18;
                upgrade.bonusStartingWorkers = 1;
                upgrade.startingResourceBonus = 0f;
                upgrade.kingdomStage = 1;
                upgrade.description = "Field one extra worker in every battle.";
            });

            database.units = new List<UnitDefinition> { worker, swordsman, spearman, archer };
            database.buildings = new List<BuildingDefinition> { townHall, barracks, watchtower };
            database.missions = new List<MissionDefinition>
            {
                mission01, mission02, mission03, mission04, mission05, mission06,
                mission07, mission08, mission09, mission10, mission11, mission12
            };
            database.skirmishes = new List<MissionDefinition> { skirmishForest, skirmishPlains, skirmishHighlands };
            database.upgrades = new List<KingdomUpgradeDefinition> { upgradeVillage, upgradeFortified, upgradeCastle, upgradeStronghold, upgradeVanguard };
            database.InvalidateCaches();

            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();

            Debug.Log($"ProjectBootstrap: content assets ready — {database.units.Count} units, {database.buildings.Count} buildings, {database.missions.Count} missions, {database.skirmishes.Count} skirmishes, {database.upgrades.Count} upgrades, difficulty profiles easy/normal/hard.");
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
            BuildMenuScenes(database);
        }

        /// <summary>
        /// Build the three metagame screens. Bootstrap is built first by <see cref="BuildScenes"/> so a
        /// screen that loads it always has it in the build.
        /// </summary>
        private static void BuildMenuScenes(GameDatabase database)
        {
            BuildCampaignMapScene(database);
            BuildSkirmishScene(database);
            BuildKingdomScene(database);
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
            // Generated painterly grass replaces the flat spec colour when the art pass has run;
            // the tint drops to white so the texture supplies the colour.
            Texture2D grassTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Resources/UI/tex_grass.png");
            if (grassTex != null)
            {
                if (grass.HasProperty("_BaseMap")) grass.SetTexture("_BaseMap", grassTex);
                if (grass.HasProperty("_BaseColor")) grass.SetColor("_BaseColor", Color.white);
                EditorUtility.SetDirty(grass);
            }
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
            camera.backgroundColor = new Color(0.16f, 0.24f, 0.15f);
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

            // Battle HUD. Built here rather than at first frame so the shipped scene carries the real
            // hierarchy, and so the layout is authored by the same code EditMode tests inspect.
            var hudGo = new GameObject("HUD");
            BattleHud hud = hudGo.AddComponent<BattleHud>();
            hud.Build(liveDatabase);
            BattleHud.EnsureEventSystem();
            bootstrap.hud = hud;

            // Player holdings.
            CreateBuilding("PlayerTownHall", new Vector3(9f, 0f, 9f), townHallDefinition, Team.Player, playerBuilding, 3.4f);
            // A finished barracks next to the town hall, clear of the farms at (16,5)/(6,16) and the
            // gold vein at (18,16), so the train buttons have a live target on first load.
            CreateBuilding("PlayerBarracks", new Vector3(16f, 0f, 12f), barracksDefinition, Team.Player, playerBuilding, 3.1f);
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

            AudioManager audio = CreateAudioManager();
            audio.music = "Music_Battle";

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
            // Landing is the campaign map: the player picks a mission there, and the battle scene's
            // own GameBootstrap (loadBattleSceneOnStart=false) composes the fight it was handed.
            bootstrap.battleSceneName = "CampaignMap";
            bootstrap.database = liveDatabase;
            bootstrap.economyBalance = liveDatabase.economy;

            CreateAudioManager();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, BootstrapScenePath))
            {
                throw new InvalidOperationException($"Failed to save the bootstrap scene to '{BootstrapScenePath}'.");
            }

            Debug.Log($"ProjectBootstrap: bootstrap scene built at '{BootstrapScenePath}'.");
        }

        private static void BuildCampaignMapScene(GameDatabase database)
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Same reload rule as the battle scene: the instance passed in did not survive the swap.
            GameDatabase liveDatabase = ReloadDatabase() ?? database;

            var go = new GameObject("CampaignMapScreen");
            CampaignMapScreen screen = go.AddComponent<CampaignMapScreen>();
            screen.database = liveDatabase;
            screen.Build(liveDatabase);
            BattleHud.EnsureEventSystem();

            CreateAudioManager();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, CampaignMapScenePath))
            {
                throw new InvalidOperationException($"Failed to save the campaign map scene to '{CampaignMapScenePath}'.");
            }

            Debug.Log($"ProjectBootstrap: campaign map scene built at '{CampaignMapScenePath}'.");
        }

        private static void BuildSkirmishScene(GameDatabase database)
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Same reload rule as the battle scene: the instance passed in did not survive the swap.
            GameDatabase liveDatabase = ReloadDatabase() ?? database;

            var go = new GameObject("SkirmishScreen");
            SkirmishScreen screen = go.AddComponent<SkirmishScreen>();
            screen.database = liveDatabase;
            screen.Build(liveDatabase);
            BattleHud.EnsureEventSystem();

            CreateAudioManager();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, SkirmishScenePath))
            {
                throw new InvalidOperationException($"Failed to save the skirmish scene to '{SkirmishScenePath}'.");
            }

            Debug.Log($"ProjectBootstrap: skirmish scene built at '{SkirmishScenePath}'.");
        }

        private static void BuildKingdomScene(GameDatabase database)
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Same reload rule as the battle scene: the instance passed in did not survive the swap.
            GameDatabase liveDatabase = ReloadDatabase() ?? database;

            var go = new GameObject("KingdomScreen");
            KingdomScreen screen = go.AddComponent<KingdomScreen>();
            screen.database = liveDatabase;
            screen.Build(liveDatabase);
            BattleHud.EnsureEventSystem();

            CreateAudioManager();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, KingdomScenePath))
            {
                throw new InvalidOperationException($"Failed to save the kingdom scene to '{KingdomScenePath}'.");
            }

            Debug.Log($"ProjectBootstrap: kingdom scene built at '{KingdomScenePath}'.");
        }

        private static void ConfigureBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootstrapScenePath, true),
                new EditorBuildSettingsScene(BattleScenePath, true),
                new EditorBuildSettingsScene(CampaignMapScenePath, true),
                new EditorBuildSettingsScene(SkirmishScenePath, true),
                new EditorBuildSettingsScene(KingdomScenePath, true)
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

        /// <summary>
        /// One audio object per scene. It owns the scene's single <see cref="AudioListener"/>, so a
        /// scene authored without it plays nothing however many clips are on disk.
        /// </summary>
        private static AudioManager CreateAudioManager()
        {
            var go = new GameObject("AudioManager");
            // The listener is authored, not only added at runtime, so the shipped scene is audible
            // with no code path in between and every one of the five scenes has exactly one.
            go.AddComponent<AudioListener>();

            AudioManager audio = go.AddComponent<AudioManager>();
            audio.music = "Music_Menu";
            return audio;
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

            GameObject body = ModelLibrary.Spawn(ModelLibrary.UnitModel(definition.id), go.transform);
            if (body != null)
            {
                var capsule = body.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 0.9f, 0f);
                capsule.radius = 0.45f;
                capsule.height = 1.8f;
                if (team == Team.Enemy) ModelLibrary.TintMaterials(body, "Cloth", new Color(0.55f, 0.16f, 0.12f));
            }
            else
            {
                body = GameObject.CreatePrimitive(definition.IsCombatUnit ? PrimitiveType.Capsule : PrimitiveType.Cylinder);
                body.name = "Body";
                body.transform.SetParent(go.transform, false);
                body.transform.localScale = definition.IsCombatUnit ? new Vector3(0.7f, 0.75f, 0.7f) : new Vector3(0.65f, 0.5f, 0.65f);
                body.transform.localPosition = new Vector3(0f, definition.IsCombatUnit ? 0.75f : 0.5f, 0f);

                var renderer = body.GetComponent<MeshRenderer>();
                if (renderer != null && material != null) renderer.sharedMaterial = material;
            }

            UnitController unit = go.AddComponent<UnitController>();
            unit.team = team;
            unit.definition = definition;
        }

        /// <summary>A tree is a harvestable trunk plus a non-harvestable canopy child.</summary>
        private static void CreateTree(string name, Vector3 basePosition, Material bark, Material leaf, int capacity)
        {
            GameObject go = ModelLibrary.SpawnAt("Tree", basePosition);
            if (go == null)
            {
                go = CreatePrimitive(name, PrimitiveType.Cylinder, basePosition + new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), bark);
                GameObject canopy = CreatePrimitive($"{name}_Canopy", PrimitiveType.Sphere, basePosition + new Vector3(0f, 2.1f, 0f), new Vector3(2.4f, 1.6f, 2.4f), leaf);
                canopy.transform.SetParent(go.transform, true);
            }
            go.name = name;

            ResourceNode node = go.AddComponent<ResourceNode>();
            node.Configure(ResourceType.Wood, capacity);
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

            GameObject body = ModelLibrary.Spawn(ModelLibrary.BuildingModel(definition.id), go.transform);
            if (body != null)
            {
                var box = body.AddComponent<BoxCollider>();
                box.size = new Vector3(definition.footprint.x * 0.95f, 7f, definition.footprint.y * 0.95f);
                box.center = new Vector3(0f, 3.5f, 0f);
                if (team == Team.Enemy) ModelLibrary.TintMaterials(body, "Plaster", EnemyBuildingColor);
            }
            else
            {
                body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "Body";
                body.transform.SetParent(go.transform, false);
                body.transform.localScale = new Vector3(definition.footprint.x * 0.95f, height, definition.footprint.y * 0.95f);
                body.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);

                var renderer = body.GetComponent<MeshRenderer>();
                if (renderer != null && material != null) renderer.sharedMaterial = material;
            }

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
            GameObject go = ModelLibrary.SpawnAt(ModelLibrary.ResourceModel(resource), new Vector3(position.x, 0f, position.z));
            if (go == null) go = CreatePrimitive(name, type, position, scale, material);
            go.name = name;

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

using System.Collections.Generic;
using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>
    /// Runtime handle on the authored low-poly models living under Resources/Models.
    /// Every lookup can return null; callers keep their primitive fallback path.
    /// </summary>
    public static class ModelLibrary
    {
        private static readonly Dictionary<string, GameObject> Cache = new Dictionary<string, GameObject>();

        public static string UnitModel(string unitId)
        {
            switch (unitId)
            {
                case "worker": return "Villager";
                case "swordsman": return "Swordsman";
                case "spearman": return "Spearman";
                case "archer": return "Archer";
                default: return null;
            }
        }

        public static string BuildingModel(string buildingId)
        {
            switch (buildingId)
            {
                case "town_hall": return "TownHall";
                case "barracks": return "Barracks";
                case "watchtower": return "Watchtower";
                default: return null;
            }
        }

        public static string ResourceModel(ResourceType type)
        {
            switch (type)
            {
                case ResourceType.Wood: return "Tree";
                case ResourceType.Food: return "Farm";
                case ResourceType.Gold: return "GoldRock";
                default: return null;
            }
        }

        public static GameObject Load(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!Cache.TryGetValue(name, out GameObject prefab))
            {
                prefab = Resources.Load<GameObject>("Models/" + name);
                Cache[name] = prefab;
            }
            return prefab;
        }

        /// <summary>Instantiate a model under a parent as the standard "Body" child.</summary>
        public static GameObject Spawn(string name, Transform parent)
        {
            GameObject prefab = Load(name);
            if (prefab == null) return null;
            GameObject instance = Object.Instantiate(prefab, parent, false);
            instance.name = "Body";
            RemapMaterials(instance);
            return instance;
        }

        /// <summary>Instantiate a model as a world-root object at a position.</summary>
        public static GameObject SpawnAt(string name, Vector3 position)
        {
            GameObject prefab = Load(name);
            if (prefab == null) return null;
            GameObject instance = Object.Instantiate(prefab);
            instance.transform.position = position;
            RemapMaterials(instance);
            return instance;
        }

        // FBX-embedded materials import on whatever shader the importer picks. Rather than fight
        // the importer, every instance is re-slotted onto shared URP Lit materials created lazily
        // from the Blender palette — one material per colour for the whole game.
        private static readonly Dictionary<string, Material> PaletteMaterials = new Dictionary<string, Material>();

        private static readonly Dictionary<string, Color> Palette = new Dictionary<string, Color>
        {
            { "Stone",    new Color(0.55f, 0.53f, 0.50f) },
            { "Wood",     new Color(0.45f, 0.30f, 0.16f) },
            { "WoodDark", new Color(0.30f, 0.19f, 0.10f) },
            { "Thatch",   new Color(0.58f, 0.44f, 0.22f) },
            { "Plaster",  new Color(0.85f, 0.80f, 0.70f) },
            { "Skin",     new Color(0.85f, 0.62f, 0.45f) },
            { "Cloth",    new Color(0.25f, 0.38f, 0.62f) },
            { "Cloth2",   new Color(0.55f, 0.42f, 0.25f) },
            { "Pants",    new Color(0.32f, 0.25f, 0.18f) },
            { "Leaf",     new Color(0.24f, 0.48f, 0.22f) },
            { "Leaf2",    new Color(0.32f, 0.58f, 0.26f) },
            { "Dirt",     new Color(0.40f, 0.28f, 0.16f) },
            { "Crop",     new Color(0.62f, 0.72f, 0.25f) },
            { "Gold",     new Color(0.95f, 0.75f, 0.20f) },
            { "Metal",    new Color(0.60f, 0.62f, 0.68f) },
            { "Door",     new Color(0.28f, 0.18f, 0.10f) },
        };

        private static void RemapMaterials(GameObject instance)
        {
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    string key = slots[i] != null ? slots[i].name.Replace(" (Instance)", "") : null;
                    if (key == null || !Palette.ContainsKey(key)) continue;
                    if (!PaletteMaterials.TryGetValue(key, out Material material) || material == null)
                    {
                        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                        if (shader == null) shader = Shader.Find("Standard");
                        material = new Material(shader) { name = "Model_" + key };
                        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Palette[key]);
                        else if (material.HasProperty("_Color")) material.SetColor("_Color", Palette[key]);
                        PaletteMaterials[key] = material;
                    }
                    slots[i] = material;
                }
                renderer.sharedMaterials = slots;
            }
        }

        /// <summary>
        /// Re-colour every material slot whose name contains <paramref name="match"/>. Used to
        /// push hostile units/buildings red so teams read at a glance on the low-poly bodies.
        /// </summary>
        public static void TintMaterials(GameObject body, string match, Color color)
        {
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>())
            {
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null || !slots[i].name.Contains(match)) continue;
                    slots[i] = new Material(slots[i]);
                    if (slots[i].HasProperty("_BaseColor")) slots[i].SetColor("_BaseColor", color);
                    else if (slots[i].HasProperty("_Color")) slots[i].SetColor("_Color", color);
                }
                renderer.sharedMaterials = slots;
            }
        }
    }
}

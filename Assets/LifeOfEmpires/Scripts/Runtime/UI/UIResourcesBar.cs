using UnityEngine;
using UnityEngine.UI;

namespace LifeOfEmpires.UI
{
    /// <summary>
    /// The live resource strip along the top of the battle screen.
    ///
    /// Wood, food and gold come from the player's <see cref="ResourceManager"/> and refresh on its
    /// change event. Crowns do not: <see cref="BattleManager.HandleBattleEnded"/> writes crowns to
    /// the save file and never into the battle stockpile, so reading the stockpile would show a
    /// permanent zero. They are read from disk and refreshed when the battle ends, which is the only
    /// moment they can change during a battle.
    /// </summary>
    public class UIResourcesBar : MonoBehaviour
    {
        public Text woodLabel;
        public Text foodLabel;
        public Text goldLabel;
        public Text crownsLabel;

        private ResourceManager _resources;
        private BattleObjectiveManager _objectives;

        public void Build(Transform parent)
        {
            Image panel = UiFactory.Panel("ResourcesBar", parent, UiFactory.PanelColor);
            UiFactory.Place(panel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 100f));

            woodLabel = BuildSlot(panel.transform, "Wood", "icon_wood", 0f, 0.25f);
            foodLabel = BuildSlot(panel.transform, "Food", "icon_food", 0.25f, 0.5f);
            goldLabel = BuildSlot(panel.transform, "Gold", "icon_gold", 0.5f, 0.75f);
            crownsLabel = BuildSlot(panel.transform, "Crowns", "icon_crown", 0.75f, 1f);
        }

        private static Text BuildSlot(Transform parent, string name, string iconName, float left, float right)
        {
            // Icon sits against the slot's left edge; the count stays centred in the slot so it
            // reads the same at every stockpile width.
            Image icon = UiFactory.Icon($"{name}Icon", parent, iconName);
            UiFactory.Place(icon.rectTransform,
                new Vector2(left, 0.5f), new Vector2(left, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(20f, 0f), new Vector2(60f, 60f));

            Text label = UiFactory.Label(name, parent, name, 36, TextAnchor.MiddleCenter, UiFactory.TextColor);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(left, 0f);
            rect.anchorMax = new Vector2(right, 1f);
            rect.offsetMin = new Vector2(30f, 0f);
            rect.offsetMax = Vector2.zero;
            return label;
        }

        /// <summary>Bind to the live battle. Safe to call again; the previous subscription is dropped.</summary>
        public void Bind(BattleContext context)
        {
            Unbind();

            if (context != null)
            {
                _resources = context.PlayerResources;
                if (_resources != null) _resources.OnResourceChanged += HandleResourceChanged;
            }

            if (ServiceLocator.TryGet(out BattleManager manager) && manager != null)
            {
                _objectives = manager.objectives;
            }

            if (_objectives != null) _objectives.OnBattleEnded += HandleBattleEnded;

            RefreshStockpile();
            RefreshCrowns();
        }

        public void Unbind()
        {
            if (_resources != null) _resources.OnResourceChanged -= HandleResourceChanged;
            if (_objectives != null) _objectives.OnBattleEnded -= HandleBattleEnded;

            _resources = null;
            _objectives = null;
        }

        /// <summary>Re-render wood, food and gold from the current stockpile.</summary>
        public void RefreshStockpile()
        {
            if (woodLabel != null) woodLabel.text = $"{Stockpile(ResourceType.Wood)}";
            if (foodLabel != null) foodLabel.text = $"{Stockpile(ResourceType.Food)}";
            if (goldLabel != null) goldLabel.text = $"{Stockpile(ResourceType.Gold)}";
        }

        /// <summary>Re-render the crown count. Crowns live in the save file, not the stockpile.</summary>
        public void RefreshCrowns()
        {
            if (crownsLabel != null) crownsLabel.text = $"{SaveSystem.ReadFromDisk().crowns}";
        }

        private int Stockpile(ResourceType type) => _resources != null ? _resources.Get(type) : 0;

        private void HandleResourceChanged(ResourceType type, int before, int after) => RefreshStockpile();

        private void HandleBattleEnded(BattleOutcome outcome) => RefreshCrowns();

        private void OnDestroy() => Unbind();
    }
}

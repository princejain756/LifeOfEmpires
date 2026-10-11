using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LifeOfEmpires.UI
{
    /// <summary>
    /// The kingdom screen: the home kingdom's stage and crown balance, and one row per permanent
    /// upgrade with a Buy button.
    ///
    /// All purchase logic lives in <see cref="SaveData.PurchaseUpgrade"/>; this screen only reads the
    /// save, calls it, writes the save back, and repaints. Whether a button is enabled is a display
    /// read of the same state (crowns and ownership) — the screen never spells out the spend rule.
    /// </summary>
    public class KingdomScreen : MonoBehaviour
    {
        [Serializable]
        public struct UpgradeRow
        {
            public KingdomUpgradeDefinition Upgrade;
            public Button BuyButton;
            public Text Label;
        }

        public MenuCanvas menu;
        public GameDatabase database;
        public Text stageLabel;
        public Text crownsLabel;
        public Text noteLabel;
        public List<UpgradeRow> Rows = new List<UpgradeRow>();

        private const float RowHeight = 150f;
        private const float RowSpacing = 20f;

        public void Build(GameDatabase database)
        {
            this.database = database;
            if (this.database == null) return;

            if (menu == null) menu = gameObject.AddComponent<MenuCanvas>();
            menu.Build("Kingdom", "Spend crowns on permanent upgrades.");

            stageLabel = UiFactory.Label("Stage", menu.listRoot, string.Empty, 36, TextAnchor.MiddleLeft, UiFactory.TextColor);
            UiFactory.Place(stageLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 0f), new Vector2(-20f, 70f));

            crownsLabel = UiFactory.Label("Crowns", menu.listRoot, string.Empty, 36, TextAnchor.MiddleRight, UiFactory.AccentColor);
            UiFactory.Place(crownsLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 0f), new Vector2(-20f, 70f));

            BuildRows();
            BuildControls();
            Refresh();
        }

        /// <summary>Re-read the save and repaint the stage, the crown balance and every buy button.</summary>
        public void Refresh()
        {
            if (database == null) return;

            SaveData save = SaveSystem.ReadFromDisk();
            int stage = ProgressionManager.KingdomStage(database, save);

            if (stageLabel != null) stageLabel.text = $"Kingdom: {ProgressionManager.StageName(stage)}";
            if (crownsLabel != null) crownsLabel.text = $"Crowns {save.crowns}";
            if (noteLabel != null) noteLabel.text = $"Stage {stage} of 5";

            for (int i = 0; i < Rows.Count; i++)
            {
                UpgradeRow row = Rows[i];
                if (row.Upgrade == null) continue;

                bool owned = save.HasUpgrade(row.Upgrade.upgradeId);
                bool affordable = save.crowns >= row.Upgrade.crownCost;
                UiFactory.SetInteractable(row.BuyButton, !owned && affordable);
                if (row.Label != null)
                {
                    row.Label.text = owned
                        ? $"{row.Upgrade.title} — owned"
                        : $"{row.Upgrade.title} ({row.Upgrade.crownCost} crowns)";
                }
            }
        }

        private void Start()
        {
            if (Rows.Count == 0) Build(database);
            else Refresh();
        }

        private void OnEnable()
        {
            if (Rows.Count > 0) Refresh();
        }

        private void BuildRows()
        {
            Rows = new List<UpgradeRow>(database.upgrades != null ? database.upgrades.Count : 0);

            float y = 90f;
            if (database.upgrades == null) return;

            for (int i = 0; i < database.upgrades.Count; i++)
            {
                KingdomUpgradeDefinition upgrade = database.upgrades[i];
                if (upgrade == null) continue;

                RectTransform rowRoot = menu.NewRow($"UpgradeRow_{upgrade.upgradeId}");
                UiFactory.Place(rowRoot,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -y), new Vector2(-20f, RowHeight));

                Text label = UiFactory.Label("Label", rowRoot, upgrade.title, 28, TextAnchor.MiddleLeft, UiFactory.TextColor);
                UiFactory.Place(label.rectTransform,
                    new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                    new Vector2(-120f, 0f), new Vector2(-240f, 0f));

                int index = Rows.Count;
                Button buy = UiFactory.Button("Buy", rowRoot, "Buy", 28, () => Buy(index));
                UiFactory.Place(buy.GetComponent<RectTransform>(),
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-16f, 0f), new Vector2(200f, 100f));

                Rows.Add(new UpgradeRow { Upgrade = upgrade, BuyButton = buy, Label = label });
                y += RowHeight + RowSpacing;
            }
        }

        private void BuildControls()
        {
            noteLabel = UiFactory.Label("Note", menu.listRoot, string.Empty, 28, TextAnchor.MiddleLeft, UiFactory.AccentColor);
            UiFactory.Place(noteLabel.rectTransform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 130f), new Vector2(-20f, 70f));

            Button back = UiFactory.Button("BackButton", menu.listRoot, "Back", 30, () => MenuCanvas.LoadScene("CampaignMap"));
            UiFactory.Place(back.GetComponent<RectTransform>(),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 20f), new Vector2(-20f, 100f));
        }

        private void Buy(int index)
        {
            if (index < 0 || index >= Rows.Count) return;

            SaveData save = SaveSystem.ReadFromDisk();
            if (save.PurchaseUpgrade(Rows[index].Upgrade)) SaveSystem.WriteToDisk(save);
            Refresh();
        }
    }
}

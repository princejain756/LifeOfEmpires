using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LifeOfEmpires.Audio;

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
        public Button musicButton;
        public Button effectsButton;
        public List<UpgradeRow> Rows = new List<UpgradeRow>();

        private const float RowHeight = 150f;
        private const float RowSpacing = 20f;

        /// <summary>Discrete volume stops offered per channel: 20/40/60/80/100%, then off.</summary>
        private const int VolumeSteps = 5;

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

            Image crownIcon = UiFactory.Icon("CrownIcon", menu.listRoot, "icon_crown");
            UiFactory.Place(crownIcon.rectTransform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-196f, -12f), new Vector2(52f, 52f));

            BuildRows();
            BuildControls();
            Refresh();
            SceneTransition.FadeInOnLoad();
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

            RefreshAudioRowLabels();
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
                Button buy = UiFactory.Button("Buy", rowRoot, "Buy", 28, () => Buy(index), "icon_crown");
                UiFactory.Place(buy.GetComponent<RectTransform>(),
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-16f, 0f), new Vector2(200f, 100f));

                Rows.Add(new UpgradeRow { Upgrade = upgrade, BuyButton = buy, Label = label });
                y += RowHeight + RowSpacing;
            }
        }

        private void BuildControls()
        {
            // Audio rows and the note sit below the upgrade rows in the scroll content: the list is
            // top-anchored, so they take explicit y offsets past the last upgrade (rows end ~940).
            RectTransform musicRow = menu.NewRow("MusicRow");
            UiFactory.Place(musicRow,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -970f), new Vector2(-20f, 110f));
            musicButton = UiFactory.SettingRow(musicRow, "Music", "Music", "Music: 60%", CycleMusic);

            RectTransform effectsRow = menu.NewRow("EffectsRow");
            UiFactory.Place(effectsRow,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -1090f), new Vector2(-20f, 110f));
            effectsButton = UiFactory.SettingRow(effectsRow, "Effects", "Effects", "Effects: 80%", CycleEffects);

            noteLabel = UiFactory.Label("Note", menu.listRoot, string.Empty, 28, TextAnchor.MiddleLeft, UiFactory.AccentColor);
            UiFactory.Place(noteLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -1210f), new Vector2(-20f, 70f));

            Button back = UiFactory.Button("BackButton", menu.footerRoot, "Back", 30, () => MenuCanvas.LoadScene("CampaignMap"));
            UiFactory.Place(back.GetComponent<RectTransform>(),
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -8f), new Vector2(-20f, 110f));

            menu.SetContentHeight(1300f);
        }

        /// <summary>Step the music volume through 20/40/60/80/100% and then off, wrapping to 20%.</summary>
        private void CycleMusic()
        {
            SaveData save = SaveSystem.ReadFromDisk();
            int step = NextVolumeStep(save.musicVolume, save.musicMuted);
            bool muted = step > VolumeSteps;
            float volume = muted ? 1f : step / (float)VolumeSteps;

            save.musicVolume = volume;
            save.musicMuted = muted;
            SaveSystem.WriteToDisk(save);

            if (AudioManager.Current != null) AudioManager.Current.SetMusicVolume(volume, muted);
            RefreshAudioRowLabels();
        }

        /// <summary>Step the sound-effect volume through the same stops.</summary>
        private void CycleEffects()
        {
            SaveData save = SaveSystem.ReadFromDisk();
            int step = NextVolumeStep(save.sfxVolume, save.sfxMuted);
            bool muted = step > VolumeSteps;
            float volume = muted ? 1f : step / (float)VolumeSteps;

            save.sfxVolume = volume;
            save.sfxMuted = muted;
            SaveSystem.WriteToDisk(save);

            if (AudioManager.Current != null) AudioManager.Current.SetEffectVolume(volume, muted);
            RefreshAudioRowLabels();
        }

        /// <summary>
        /// The next stop in the cycle. A muted channel seeds from zero so pressing "off" returns to the
        /// lowest audible stop; seeding from the stored gain would leave the button stuck on "off"
        /// forever, because muted always stores the maximum gain.
        /// </summary>
        private static int NextVolumeStep(float volume, bool muted)
        {
            int step = muted ? 0 : Mathf.RoundToInt(Mathf.Clamp01(volume) * VolumeSteps);
            return step + 1;
        }

        /// <summary>Write both channel labels from the save; called on build, and after every cycle.</summary>
        private void RefreshAudioRowLabels()
        {
            SaveData save = SaveSystem.ReadFromDisk();

            if (musicButton != null) musicButton.GetComponentInChildren<Text>().text = VolumeLabel("Music", save.musicVolume, save.musicMuted);
            if (effectsButton != null) effectsButton.GetComponentInChildren<Text>().text = VolumeLabel("Effects", save.sfxVolume, save.sfxMuted);
        }

        private static string VolumeLabel(string channel, float volume, bool muted)
        {
            return muted ? $"{channel}: off" : $"{channel}: {Mathf.RoundToInt(Mathf.Clamp01(volume) * 100f)}%";
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

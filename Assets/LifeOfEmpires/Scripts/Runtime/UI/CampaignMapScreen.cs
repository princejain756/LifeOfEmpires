using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LifeOfEmpires.UI
{
    /// <summary>
    /// The campaign map: one row per authored mission, grouped under its territory, with the stars the
    /// player has recorded and the lock state the unlock rule reports. It reads state and calls into
    /// <see cref="CampaignManager"/> — it never re-implements the unlock rule or star scoring.
    ///
    /// Locked missions are shown, not hidden: spec §11 requires the map to represent the kingdoms still
    /// ahead. Selecting a row only shows its briefing; playing is a separate, explicit button, so a
    /// mis-tap cannot drop the player into a battle they cannot win.
    /// </summary>
    public class CampaignMapScreen : MonoBehaviour
    {
        [Serializable]
        public struct MissionRow
        {
            public MissionDefinition Mission;
            public Button Button;
            public Text TitleLabel;
            public Text StarsLabel;
            public Image LockIcon;
        }

        [Serializable]
        public struct State
        {
            public List<MissionRow> Rows;
            public int SelectedIndex;
        }

        public MenuCanvas menu;
        public GameDatabase database;
        public State state;
        public Button playButton;

        private const float RowHeight = 150f;
        private const float RowSpacing = 20f;
        private const float HeaderHeight = 60f;

        public void Build(GameDatabase database)
        {
            this.database = database;
            if (this.database == null) return;

            if (menu == null) menu = gameObject.AddComponent<MenuCanvas>();
            menu.Build("Campaign", "Choose a mission.");

            BuildRows();
            BuildControls();
            Refresh();
        }

        /// <summary>Re-read the save and repaint every row. Called from Start and whenever the map is shown.</summary>
        public void Refresh()
        {
            if (database == null || state.Rows == null) return;

            SaveData save = SaveSystem.ReadFromDisk();
            for (int i = 0; i < state.Rows.Count; i++)
            {
                MissionRow row = state.Rows[i];
                if (row.Mission == null) continue;

                bool unlocked = CampaignManager.IsUnlocked(save, database, row.Mission);
                UiFactory.SetInteractable(row.Button, unlocked);
                if (row.TitleLabel != null) row.TitleLabel.text = row.Mission.title;
                if (row.StarsLabel != null) row.StarsLabel.text = unlocked ? CampaignManager.StarLabel(save, row.Mission) : "Locked";
                if (row.LockIcon != null) row.LockIcon.enabled = !unlocked && row.LockIcon.sprite != null;
            }
        }

        /// <summary>Select a row by index. A locked row can still be selected so its briefing can be read.</summary>
        public void SelectRow(int index)
        {
            if (state.Rows == null || index < 0 || index >= state.Rows.Count) return;

            state.SelectedIndex = index;
            MissionDefinition mission = state.Rows[index].Mission;
            if (mission == null || menu == null || menu.subtitleLabel == null) return;

            menu.subtitleLabel.text = string.IsNullOrEmpty(mission.briefing) ? mission.title : mission.briefing;
        }

        /// <summary>
        /// Park the selected mission for the next battle and open the battle scene. Does nothing when
        /// nothing is selected or the selection is still locked.
        /// </summary>
        public void PlaySelected()
        {
            if (state.Rows == null) return;
            if (state.SelectedIndex < 0 || state.SelectedIndex >= state.Rows.Count) return;

            MissionDefinition mission = state.Rows[state.SelectedIndex].Mission;
            if (mission == null) return;

            SaveData save = SaveSystem.ReadFromDisk();
            if (!CampaignManager.IsUnlocked(save, database, mission)) return;

            CampaignManager.SelectMission(mission.missionId);
            MenuCanvas.LoadScene("Battle");
        }

        private void Start()
        {
            if (state.Rows == null || state.Rows.Count == 0) Build(database);
            else Refresh();
        }

        private void OnEnable()
        {
            if (state.Rows != null && state.Rows.Count > 0) Refresh();
        }

        private void BuildRows()
        {
            state.Rows = new List<MissionRow>();
            state.SelectedIndex = -1;

            float y = 0f;
            string currentTerritory = null;
            for (int i = 0; i < database.missions.Count; i++)
            {
                MissionDefinition mission = database.missions[i];
                if (mission == null) continue;

                string territory = CampaignManager.TerritoryOf(mission);
                if (!string.Equals(territory, currentTerritory, StringComparison.OrdinalIgnoreCase))
                {
                    currentTerritory = territory;
                    Text header = UiFactory.Label($"Header_{i}", menu.listRoot, $"Campaign — {territory}", 34, TextAnchor.MiddleLeft, UiFactory.AccentColor);
                    UiFactory.Place(header.rectTransform,
                        new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                        new Vector2(0f, -y), new Vector2(-20f, HeaderHeight));
                    y += HeaderHeight + RowSpacing;
                }

                RectTransform rowRoot = menu.NewRow($"MissionRow_{mission.missionId}");
                UiFactory.Place(rowRoot,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -y), new Vector2(-20f, RowHeight));

                int index = state.Rows.Count;
                Button button = UiFactory.Button($"Mission_{mission.missionId}", rowRoot, mission.title, 30, () => SelectRow(index), "icon_scroll");
                UiFactory.Place(button.GetComponent<RectTransform>(),
                    new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                    new Vector2(-120f, 0f), new Vector2(-240f, 0f));

                Image lockIcon = UiFactory.Icon("LockIcon", rowRoot, "icon_lock");
                UiFactory.Place(lockIcon.rectTransform,
                    new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                    new Vector2(-16f, 20f), new Vector2(48f, 48f));

                Image starIcon = UiFactory.Icon("StarIcon", rowRoot, "icon_star");
                UiFactory.Place(starIcon.rectTransform,
                    new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                    new Vector2(-206f, -16f), new Vector2(44f, 44f));

                Text stars = UiFactory.Label("Stars", rowRoot, string.Empty, 28, TextAnchor.MiddleRight, UiFactory.TextColor);
                UiFactory.Place(stars.rectTransform,
                    new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                    new Vector2(-16f, -16f), new Vector2(190f, 60f));

                state.Rows.Add(new MissionRow
                {
                    Mission = mission,
                    Button = button,
                    TitleLabel = button.GetComponentInChildren<Text>(),
                    StarsLabel = stars,
                    LockIcon = lockIcon
                });

                y += RowHeight + RowSpacing;
            }

            menu.SetContentHeight(y + 20f);
        }

        private void BuildControls()
        {
            // Play and the nav row sit on the fixed footer: the mission list scrolls beneath them
            // and a tap on them can never be buried under a late mission row.
            playButton = UiFactory.Button("PlayButton", menu.footerRoot, "Play", 36, PlaySelected, "icon_attack");
            UiFactory.Place(playButton.GetComponent<RectTransform>(),
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -8f), new Vector2(-20f, 120f));

            RectTransform nav = UiFactory.Rect("Nav", menu.footerRoot);
            UiFactory.Place(nav,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -140f), new Vector2(-20f, 110f));

            // The map is already showing, so the Campaign button is visibly inert rather than a
            // dead-but-hot control that would appear to do nothing.
            Button campaign = UiFactory.Button("CampaignButton", nav, "Campaign", 30, null);
            PlaceThird(campaign, 0);
            UiFactory.SetInteractable(campaign, false);

            Button kingdom = UiFactory.Button("KingdomButton", nav, "Kingdom", 30, () => MenuCanvas.LoadScene("Kingdom"));
            PlaceThird(kingdom, 1);

            Button skirmish = UiFactory.Button("SkirmishButton", nav, "Skirmish", 30, () => MenuCanvas.LoadScene("Skirmish"));
            PlaceThird(skirmish, 2);
        }

        private static void PlaceThird(Button button, int slot)
        {
            float min = slot / 3f;
            float max = (slot + 1) / 3f;
            UiFactory.Place(button.GetComponent<RectTransform>(),
                new Vector2(min, 0f), new Vector2(max, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-8f, 0f));
        }
    }
}

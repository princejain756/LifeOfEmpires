using UnityEngine;
using UnityEngine.UI;

namespace LifeOfEmpires.UI
{
    /// <summary>
    /// The skirmish screen: pick a curated map and difficulty, then start a quick-play battle that is
    /// never gated by campaign progress (spec §12). The choice is written through
    /// <see cref="SkirmishComposer"/>, so the screen never owns the persisted settings shape, and the
    /// anti-exploit reward rule is shown rather than buried.
    /// </summary>
    public class SkirmishScreen : MonoBehaviour
    {
        public MenuCanvas menu;
        public GameDatabase database;
        public Button[] mapButtons = new Button[3];
        public Button[] difficultyButtons = new Button[3];
        public Text summaryLabel;
        public Text ruleLabel;
        public Button startButton;

        private SkirmishSettings _settings;

        private const float RowHeight = 110f;

        public void Build(GameDatabase database)
        {
            this.database = database;
            if (this.database == null) return;

            if (menu == null) menu = gameObject.AddComponent<MenuCanvas>();
            menu.Build("Skirmish", "Quick play, untied to the campaign.");

            _settings = SkirmishComposer.ReadSettings(SaveSystem.ReadFromDisk());

            BuildOptionRow("Map", 0f, mapButtons, i => SelectMap((SkirmishMap)i));
            BuildOptionRow("Difficulty", 200f, difficultyButtons, i => SelectDifficulty((DifficultyTier)i));

            summaryLabel = UiFactory.Label("Summary", menu.listRoot, string.Empty, 34, TextAnchor.MiddleLeft, UiFactory.TextColor);
            UiFactory.Place(summaryLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -400f), new Vector2(-20f, 80f));

            ruleLabel = UiFactory.Label("Rule", menu.listRoot, "First 5 wins pay crowns; later wins are for practice.", 26, TextAnchor.UpperLeft, UiFactory.AccentColor);
            UiFactory.Place(ruleLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -480f), new Vector2(-20f, 90f));

            BuildControls();
            Refresh();
        }

        /// <summary>Re-read the save and repaint the resolved skirmish and the current-choice markers.</summary>
        public void Refresh()
        {
            if (database == null) return;

            MissionDefinition skirmish = SkirmishComposer.Resolve(database, _settings);
            int reward = SkirmishComposer.CrownReward(database, skirmish, SaveSystem.ReadFromDisk());
            if (summaryLabel != null)
            {
                summaryLabel.text = skirmish != null
                    ? $"{skirmish.title} — {reward} crowns"
                    : "No skirmish maps authored.";
            }

            for (int i = 0; i < mapButtons.Length; i++)
            {
                UiFactory.SetInteractable(mapButtons[i], (SkirmishMap)i != _settings.map);
            }

            for (int i = 0; i < difficultyButtons.Length; i++)
            {
                UiFactory.SetInteractable(difficultyButtons[i], (DifficultyTier)i != _settings.difficulty);
            }
        }

        public void SelectMap(SkirmishMap map)
        {
            _settings.map = map;
            Persist();
            Refresh();
        }

        public void SelectDifficulty(DifficultyTier tier)
        {
            _settings.difficulty = tier;
            Persist();
            Refresh();
        }

        /// <summary>Park the resolved skirmish and open the battle scene. A skirmish ignores unlocks.</summary>
        public void StartSkirmish()
        {
            MissionDefinition skirmish = SkirmishComposer.Resolve(database, _settings);
            if (skirmish == null) return;

            CampaignManager.SelectMission(skirmish.missionId);
            MenuCanvas.LoadScene("Battle");
        }

        private void Start()
        {
            if (mapButtons == null || mapButtons.Length == 0 || mapButtons[0] == null) Build(database);
            else
            {
                _settings = SkirmishComposer.ReadSettings(SaveSystem.ReadFromDisk());
                Refresh();
            }
        }

        private void Persist()
        {
            SaveData save = SaveSystem.ReadFromDisk();
            SkirmishComposer.WriteSettings(save, _settings);
            SaveSystem.WriteToDisk(save);
        }

        private void BuildOptionRow(string label, float y, Button[] buttons, System.Action<int> onSelect)
        {
            Text caption = UiFactory.Label($"{label}Label", menu.listRoot, label, 30, TextAnchor.MiddleLeft, UiFactory.AccentColor);
            UiFactory.Place(caption.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -y), new Vector2(-20f, 60f));

            string[] names = label == "Map"
                ? new[] { "Greenwood", "Stonebridge", "Iron Hills" }
                : new[] { "Easy", "Normal", "Hard" };

            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                buttons[i] = UiFactory.Button($"{label}_{names[i]}", menu.listRoot, names[i], 28, () => onSelect(index));
                float min = i / 3f;
                float max = (i + 1) / 3f;
                UiFactory.Place(buttons[i].GetComponent<RectTransform>(),
                    new Vector2(min, 1f), new Vector2(max, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -(y + 60f)), new Vector2(-12f, RowHeight));
            }
        }

        private void BuildControls()
        {
            startButton = UiFactory.Button("StartButton", menu.listRoot, "Start", 34, StartSkirmish);
            UiFactory.Place(startButton.GetComponent<RectTransform>(),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 130f), new Vector2(-20f, 110f));

            Button back = UiFactory.Button("BackButton", menu.listRoot, "Back", 30, () => MenuCanvas.LoadScene("CampaignMap"));
            UiFactory.Place(back.GetComponent<RectTransform>(),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 20f), new Vector2(-20f, 100f));
        }
    }
}

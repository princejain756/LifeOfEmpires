using UnityEngine;
using UnityEngine.UI;

namespace LifeOfEmpires.UI
{
    /// <summary>
    /// The end-of-battle card: outcome, stars and the mission's crown reward, plus the two ways out
    /// of the battle scene.
    ///
    /// It awards nothing. <see cref="BattleManager.HandleBattleEnded"/> already records the result and
    /// adds the crowns to the save file; a second award here would pay the player twice, so this panel
    /// only reports what was already banked. Stars arrive from
    /// <see cref="BattleObjectiveManager.OnStarsChanged"/>, which fires before the end event, so the
    /// card is complete by the time it is shown.
    /// </summary>
    public class UIResultsPanel : MonoBehaviour
    {
        private RectTransform _panel;
        private BattleObjectiveManager _objectives;
        private BattleManager _battle;
        private GameDatabase _database;

        private int _earnedStars;
        private int _totalStars;

        public Text titleLabel;
        public Text detailLabel;
        public Text starsLabel;
        public Text crownsLabel;
        public Button returnButton;
        public Button nextMissionButton;

        public BattleOutcome Outcome { get; private set; } = BattleOutcome.InProgress;

        public bool IsShown => _panel != null && _panel.gameObject.activeSelf;

        public void Build(Transform parent)
        {
            Image backdrop = UiFactory.Panel("ResultsPanel", parent, new Color(0f, 0f, 0f, 0.72f));
            _panel = UiFactory.Place(backdrop.rectTransform,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-48f, -240f));

            Image card = UiFactory.Panel("Card", backdrop.transform, UiFactory.PanelColor);
            Sprite cardFace = UiFactory.UiSprite("panel_wood");
            if (cardFace != null)
            {
                card.sprite = cardFace;
                card.type = Image.Type.Simple;
                card.color = Color.white;
            }
            UiFactory.Place(card.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(760f, 720f));

            titleLabel = UiFactory.Label("Title", card.transform, "Mission", 46, TextAnchor.MiddleCenter, UiFactory.TextColor);
            UiFactory.Place(titleLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -40f), new Vector2(-40f, 80f));

            detailLabel = UiFactory.Label("Detail", card.transform, string.Empty, 34, TextAnchor.MiddleCenter, UiFactory.AccentColor);
            UiFactory.Place(detailLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -130f), new Vector2(-40f, 70f));

            starsLabel = UiFactory.Label("Stars", card.transform, string.Empty, 34, TextAnchor.MiddleCenter, UiFactory.TextColor);
            UiFactory.Place(starsLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(28f, -220f), new Vector2(-40f, 60f));

            Image starIcon = UiFactory.Icon("StarIcon", card.transform, "icon_star");
            UiFactory.Place(starIcon.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(1f, 1f),
                new Vector2(-96f, -222f), new Vector2(56f, 56f));

            crownsLabel = UiFactory.Label("Crowns", card.transform, string.Empty, 34, TextAnchor.MiddleCenter, UiFactory.AccentColor);
            UiFactory.Place(crownsLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(28f, -290f), new Vector2(-40f, 60f));

            Image crownIcon = UiFactory.Icon("CrownIcon", card.transform, "icon_crown");
            UiFactory.Place(crownIcon.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(1f, 1f),
                new Vector2(-116f, -292f), new Vector2(56f, 56f));

            returnButton = UiFactory.Button("Return", card.transform, "Return", 32, ReturnToBootstrap);
            UiFactory.Place(returnButton.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 40f), new Vector2(300f, 100f));

            nextMissionButton = UiFactory.Button("NextMission", card.transform, "Next Mission", 32, GoToNextMission);
            UiFactory.Place(nextMissionButton.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 160f), new Vector2(300f, 100f));

            Hide();
        }

        /// <summary>Bind to the objective manager that ends battles and the manager that owns scene flow.</summary>
        public void Bind(BattleObjectiveManager objectives, BattleManager battle, GameDatabase database)
        {
            Unbind();

            _objectives = objectives;
            _battle = battle;
            _database = database;

            if (_objectives != null)
            {
                _objectives.OnBattleEnded += HandleBattleEnded;
                _objectives.OnStarsChanged += HandleStarsChanged;
            }

            RefreshNextMissionButton();
        }

        public void Unbind()
        {
            if (_objectives != null)
            {
                _objectives.OnBattleEnded -= HandleBattleEnded;
                _objectives.OnStarsChanged -= HandleStarsChanged;
            }

            _objectives = null;
            _battle = null;
            _database = null;
        }

        public void Hide()
        {
            Outcome = BattleOutcome.InProgress;
            if (_panel != null) _panel.gameObject.SetActive(false);
        }

        private void HandleBattleEnded(BattleOutcome outcome)
        {
            Outcome = outcome;

            MissionDefinition mission = BattleContext.Current != null ? BattleContext.Current.Mission : null;
            string missionTitle = mission != null ? mission.title : "Battle";

            if (titleLabel != null) titleLabel.text = missionTitle;

            if (outcome == BattleOutcome.Victory)
            {
                if (detailLabel != null)
                {
                    detailLabel.color = UiFactory.AccentColor;
                    detailLabel.text = "Victory";
                }

                if (crownsLabel != null)
                {
                    int reward = mission != null ? mission.crownReward : 0;
                    crownsLabel.text = $"Crowns +{reward}";
                }
            }
            else
            {
                if (detailLabel != null)
                {
                    detailLabel.color = UiFactory.DangerColor;
                    detailLabel.text = "Defeat — your town hall fell.";
                }

                if (crownsLabel != null) crownsLabel.text = "No crowns this time";
            }

            RefreshStarLabel();
            RefreshNextMissionButton();

            if (_panel != null) _panel.gameObject.SetActive(true);
        }

        private void HandleStarsChanged(int earned, int total)
        {
            _earnedStars = earned;
            _totalStars = total;
            RefreshStarLabel();
        }

        private void RefreshStarLabel()
        {
            if (starsLabel != null) starsLabel.text = $"Stars {_earnedStars}/{_totalStars}";
        }

        private void RefreshNextMissionButton()
        {
            if (nextMissionButton == null) return;

            MissionDefinition next = CampaignManager.NextPlayableMission(_database, SaveSystem.ReadFromDisk());
            bool available = next != null;
            nextMissionButton.gameObject.SetActive(available);
            if (!available) return;

            Text label = nextMissionButton.GetComponentInChildren<Text>();
            if (label != null) label.text = $"Next: {next.title}";
        }

        /// <summary>Leave the battle scene and land on the campaign map.</summary>
        public void ReturnToBootstrap()
        {
            if (_battle != null)
            {
                _battle.ReturnToCampaignMap();
                return;
            }

            UnityEngine.SceneManagement.SceneManager.LoadScene("CampaignMap");
        }

        /// <summary>
        /// Play the next unlocked mission. The choice is parked on
        /// <see cref="CampaignManager.PendingMissionId"/> so the bootstrap scene, which cannot carry
        /// arguments across a scene load, picks it up when it composes the next battle.
        /// </summary>
        public void GoToNextMission()
        {
            MissionDefinition next = CampaignManager.NextPlayableMission(_database, SaveSystem.ReadFromDisk());
            if (next == null) return;

            CampaignManager.SelectMission(next.missionId);
            ReturnToBootstrap();
        }

        private void OnDestroy() => Unbind();
    }
}

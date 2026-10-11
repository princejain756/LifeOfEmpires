using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LifeOfEmpires.UI
{
    /// <summary>
    /// Production controls for one selected building.
    ///
    /// It offers every <see cref="UnitDefinition"/> flagged as a combat unit, not just the building's
    /// own <see cref="BuildingDefinition.producesUnitId"/>. A barracks has a single production slot
    /// and a single charge path, so its extra trainable units go through
    /// <see cref="BuildingController.TryTrain"/> — which this panel never second-guesses: it hands
    /// over the definition and the payer, and shows whatever refusal reason comes back.
    /// </summary>
    public class UIProductionPanel : MonoBehaviour
    {
        private const float RowHeight = 76f;

        private readonly List<string> _order = new List<string>(8);
        private readonly Dictionary<string, UnitDefinition> _definitions = new Dictionary<string, UnitDefinition>(8);
        private readonly Dictionary<string, Button> _buttons = new Dictionary<string, Button>(8);

        private RectTransform _panel;
        private ResourceManager _resources;
        private BuildingController _building;

        public Text titleLabel;
        public Text progressLabel;
        public Text statusLabel;
        public Image progressFill;

        /// <summary>The building currently on show, or null when the panel is hidden.</summary>
        public BuildingController Building => _building;

        public bool IsShown => _building != null;

        public void Build(Transform parent, GameDatabase database)
        {
            Image panel = UiFactory.Panel("ProductionPanel", parent, UiFactory.PanelColor);
            _panel = UiFactory.Place(panel.rectTransform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 16f), new Vector2(600f, 300f));

            titleLabel = UiFactory.Label("Title", panel.transform, "Production", 32, TextAnchor.MiddleCenter, UiFactory.AccentColor);
            UiFactory.Place(titleLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -10f), new Vector2(-24f, 48f));

            // Progress is a track plus a child whose width follows TrainingProgress. A sprite-less
            // Image cannot use fillAmount (Image falls back to a plain quad without a sprite), so the
            // bar is driven by anchors instead.
            Image track = UiFactory.Panel("ProgressTrack", panel.transform, new Color(0f, 0f, 0f, 0.45f));
            UiFactory.Place(track.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -62f), new Vector2(-32f, 22f));

            progressFill = UiFactory.Panel("ProgressFill", track.transform, UiFactory.AccentColor);
            RectTransform fillRect = progressFill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            progressLabel = UiFactory.Label("Progress", panel.transform, string.Empty, 24, TextAnchor.MiddleLeft, UiFactory.TextColor);
            UiFactory.Place(progressLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -88f), new Vector2(-32f, 34f));

            int row = 0;
            if (database != null && database.units != null)
            {
                for (int i = 0; i < database.units.Count; i++)
                {
                    UnitDefinition definition = database.units[i];
                    if (definition == null || !definition.IsCombatUnit) continue;
                    if (string.IsNullOrEmpty(definition.id)) continue;

                    _order.Add(definition.id);
                    _definitions[definition.id] = definition;

                    string id = definition.id;
                    Button button = UiFactory.Button(
                        $"Train_{id}",
                        panel.transform,
                        $"{definition.displayName}  {definition.cost}",
                        26,
                        () => Train(id));

                    UiFactory.Place(button.GetComponent<RectTransform>(),
                        new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                        new Vector2(0f, -132f - (row * (RowHeight + 6f))), new Vector2(-32f, RowHeight));

                    _buttons[id] = button;
                    row++;
                }
            }

            statusLabel = UiFactory.Label("Status", panel.transform, string.Empty, 24, TextAnchor.MiddleLeft, UiFactory.DangerColor);
            UiFactory.Place(statusLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -140f - (row * (RowHeight + 6f))), new Vector2(-32f, 40f));

            _panel.sizeDelta = new Vector2(600f, 190f + (row * (RowHeight + 6f)) + 12f);
            Hide();
        }

        public void Bind(ResourceManager resources)
        {
            _resources = resources;
            Refresh();
        }

        /// <summary>Show production for <paramref name="building"/>. Null hides the panel.</summary>
        public void Show(BuildingController building)
        {
            _building = building;
            if (_panel != null) _panel.gameObject.SetActive(building != null);
            if (building != null && titleLabel != null)
            {
                titleLabel.text = building.Definition != null ? building.Definition.displayName : "Production";
            }

            if (statusLabel != null) statusLabel.text = string.Empty;
            Refresh();
        }

        public void Hide() => Show(null);

        /// <summary>Start training one unit. Returns false and surfaces the refusal reason when refused.</summary>
        public bool Train(string unitId)
        {
            if (_building == null || !_definitions.TryGetValue(unitId, out UnitDefinition definition))
            {
                if (statusLabel != null) statusLabel.text = "Nothing selected to train from";
                return false;
            }

            if (!_building.TryTrain(definition, _resources, out string reason))
            {
                if (statusLabel != null) statusLabel.text = string.IsNullOrEmpty(reason) ? "Training refused" : reason;
                Refresh();
                return false;
            }

            if (statusLabel != null) statusLabel.text = $"Training {definition.displayName}";
            Refresh();
            return true;
        }

        /// <summary>Re-render progress and affordability from the live building.</summary>
        public void Refresh()
        {
            bool shown = _building != null;
            if (_panel != null && _panel.gameObject.activeSelf != shown) _panel.gameObject.SetActive(shown);
            if (!shown)
            {
                if (progressLabel != null) progressLabel.text = string.Empty;
                if (progressFill != null) progressFill.rectTransform.anchorMax = new Vector2(0f, 1f);
                return;
            }

            float progress = _building != null ? _building.TrainingProgress : 0f;
            if (progressFill != null) progressFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            if (progressLabel != null)
            {
                progressLabel.text = _building.IsTraining
                    ? $"Training {_building.TrainingUnitId} — {(progress * 100f):F0}%"
                    : string.Empty;
            }

            bool busy = _building.IsTraining || !_building.IsConstructed;
            for (int i = 0; i < _order.Count; i++)
            {
                string id = _order[i];
                if (!_buttons.TryGetValue(id, out Button button)) continue;

                UnitDefinition definition = _definitions[id];
                bool affordable = _resources != null && _resources.CanAfford(definition.cost);
                UiFactory.SetInteractable(button, !busy && affordable);
            }
        }

        private void Update()
        {
            // The train buttons are live while a unit is queued, so progress must be re-rendered
            // rather than waiting for an event the production slot does not raise per frame.
            if (_building != null && _building.IsTraining) Refresh();
        }

        public Button GetButton(string unitId)
        {
            return _buttons.TryGetValue(unitId, out Button button) ? button : null;
        }

        private void OnDestroy() => Unbind();

        public void Unbind()
        {
            _resources = null;
            _building = null;
        }
    }
}

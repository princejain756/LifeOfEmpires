using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LifeOfEmpires.UI
{
    /// <summary>
    /// The build menu: one button per placeable building, priced from its own
    /// <see cref="BuildingDefinition"/>.
    ///
    /// A button does exactly one thing — hand the definition to
    /// <see cref="BuildingPlacementSystem.BeginPlacement"/>. It never moves the ghost and never
    /// confirms: the battle input controller already follows the finger with
    /// <c>UpdateGhost</c> and commits on the next tap, and a second copy of that would double-charge
    /// the player. Buttons the player cannot afford are disabled and re-checked on every resource
    /// change, because affordability is stockpile state, not menu state.
    /// </summary>
    public class UIBuildMenu : MonoBehaviour
    {
        private const float RowHeight = 84f;

        private readonly List<string> _order = new List<string>(8);
        private readonly Dictionary<string, BuildingDefinition> _definitions = new Dictionary<string, BuildingDefinition>(8);
        private readonly Dictionary<string, Button> _buttons = new Dictionary<string, Button>(8);

        private BuildingPlacementSystem _placement;
        private ResourceManager _resources;

        public Text titleLabel;

        /// <summary>Building ids this menu offers, in authored order. Town halls are excluded.</summary>
        public IReadOnlyList<string> BuildingIds => _order;

        public void Build(Transform parent, GameDatabase database)
        {
            Image panel = UiFactory.Panel("BuildMenu", parent, UiFactory.PanelColor);
            UiFactory.Place(panel.rectTransform,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-16f, 16f), new Vector2(420f, 140f));

            titleLabel = UiFactory.Label("Title", panel.transform, "Build", 30, TextAnchor.MiddleCenter, UiFactory.AccentColor);
            UiFactory.Place(titleLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -8f), new Vector2(-24f, 44f));

            int row = 0;
            if (database != null && database.buildings != null)
            {
                for (int i = 0; i < database.buildings.Count; i++)
                {
                    BuildingDefinition definition = database.buildings[i];
                    if (definition == null || definition.isTownHall) continue;
                    if (string.IsNullOrEmpty(definition.id)) continue;

                    _order.Add(definition.id);
                    _definitions[definition.id] = definition;

                    string id = definition.id;
                    Button button = UiFactory.Button(
                        $"Build_{id}",
                        panel.transform,
                        $"{definition.displayName}  {definition.cost}",
                        26,
                        () => RequestPlacement(id));

                    UiFactory.Place(button.GetComponent<RectTransform>(),
                        new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                        new Vector2(0f, -60f - (row * (RowHeight + 8f))), new Vector2(-24f, RowHeight));

                    _buttons[id] = button;
                    row++;
                }
            }

            // Grow the panel to hold however many rows the database authored.
            float height = 76f + (row * (RowHeight + 8f)) + 16f;
            panel.rectTransform.sizeDelta = new Vector2(420f, height);

            Refresh();
        }

        /// <summary>Bind to the placement system and the stockpile that decides affordability.</summary>
        public void Bind(BuildingPlacementSystem placement, ResourceManager resources)
        {
            Unbind();

            _placement = placement;
            _resources = resources;
            if (_resources != null) _resources.OnResourceChanged += HandleResourceChanged;

            Refresh();
        }

        public void Unbind()
        {
            if (_resources != null) _resources.OnResourceChanged -= HandleResourceChanged;
            _resources = null;
            _placement = null;
        }

        /// <summary>Start placement of a building the menu offers. False when the id is unknown.</summary>
        public bool RequestPlacement(string buildingId)
        {
            if (_placement == null || !_definitions.TryGetValue(buildingId, out BuildingDefinition definition)) return false;
            return _placement.BeginPlacement(definition, Team.Player);
        }

        /// <summary>Re-evaluate every button against the current stockpile.</summary>
        public void Refresh()
        {
            for (int i = 0; i < _order.Count; i++)
            {
                string id = _order[i];
                if (!_buttons.TryGetValue(id, out Button button)) continue;

                BuildingDefinition definition = _definitions[id];
                bool affordable = _resources != null && _resources.CanAfford(definition.cost);
                UiFactory.SetInteractable(button, affordable);
            }
        }

        /// <summary>True when the button for <paramref name="buildingId"/> can be tapped. False for unknown ids.</summary>
        public bool IsButtonEnabled(string buildingId)
        {
            return _buttons.TryGetValue(buildingId, out Button button) && button != null && button.interactable;
        }

        public Button GetButton(string buildingId)
        {
            return _buttons.TryGetValue(buildingId, out Button button) ? button : null;
        }

        private void HandleResourceChanged(ResourceType type, int before, int after) => Refresh();

        private void OnDestroy() => Unbind();
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LifeOfEmpires.UI
{
    /// <summary>
    /// What the player currently owns, and the commands the HUD can issue against it.
    ///
    /// Every command is a thin wrapper: move and attack go through the same
    /// <see cref="UnitSelectionManager"/> and <see cref="UnitController"/> entry points the touch
    /// input uses, so a button can never do something a tap cannot, and there is no second copy of
    /// any order logic.
    ///
    /// Move and attack need a world target. Neither invents one: move heads for the camera rig's
    /// current focus point (the point under the screen centre), and attack takes the nearest living
    /// enemy to the selection's centre from <see cref="CombatSystem"/>.
    /// </summary>
    public class UISelectionPanel : MonoBehaviour
    {
        [Tooltip("How far from the selection's centre an enemy may be and still be chosen by Attack.")]
        [Min(1f)] public float attackSearchRadius = 40f;

        public Text summaryLabel;
        public Text detailLabel;
        public Image portraitIcon;
        public Button moveButton;
        public Button attackButton;
        public Button stopButton;
        public Button selectAllButton;
        public Button centerButton;

        private UnitSelectionManager _selection;
        private CombatSystem _combat;
        private IsometricCameraRig _cameraRig;

        public void Build(Transform parent)
        {
            Image panel = UiFactory.Panel("SelectionPanel", parent, UiFactory.PanelColor);
            UiFactory.Place(panel.rectTransform,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(16f, 16f), new Vector2(460f, 320f));

            // The selected unit's portrait sits in the top-left corner; the labels shift right of it.
            portraitIcon = UiFactory.Icon("Portrait", panel.transform, null);
            UiFactory.Place(portraitIcon.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -16f), new Vector2(96f, 96f));

            summaryLabel = UiFactory.Label("Summary", panel.transform, "No units selected", 34, TextAnchor.MiddleLeft, UiFactory.TextColor);
            UiFactory.Place(summaryLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(56f, -16f), new Vector2(-144f, 60f));

            detailLabel = UiFactory.Label("Detail", panel.transform, string.Empty, 26, TextAnchor.MiddleLeft, UiFactory.AccentColor);
            UiFactory.Place(detailLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(56f, -80f), new Vector2(-144f, 48f));

            moveButton = BuildCommandButton(panel.transform, "Move", "icon_move", 0, () => MoveSelectionToFocus());
            attackButton = BuildCommandButton(panel.transform, "Attack", "icon_attack", 1, () => AttackNearestEnemy());
            stopButton = BuildCommandButton(panel.transform, "Stop", "icon_stop", 2, () => StopSelection());
            selectAllButton = BuildCommandButton(panel.transform, "Army", "icon_swordsman", 3, () => SelectAllCombatUnits());
            centerButton = BuildCommandButton(panel.transform, "Hall", "icon_townhall", 4, () => CenterOnTownHall());
        }

        private static Button BuildCommandButton(Transform parent, string label, string iconName, int column, System.Action onClick)
        {
            Button button = UiFactory.Button(label, parent, label, 22, onClick);
            const float buttonWidth = 84f;
            const float spacing = 8f;
            UiFactory.Place(button.GetComponent<RectTransform>(),
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(16f + (column * (buttonWidth + spacing)), 20f), new Vector2(buttonWidth, 96f));

            // Icon owns the top two-thirds; the caption shrinks to the bottom strip.
            Text caption = button.GetComponentInChildren<Text>();
            if (caption != null)
            {
                UiFactory.Place(caption.rectTransform,
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 0f), new Vector2(0f, 30f));
            }

            Image icon = UiFactory.Icon("Icon", button.transform, iconName);
            UiFactory.Place(icon.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -4f), new Vector2(56f, 56f));
            return button;
        }

        /// <summary>Bind to the live selection, combat funnel and camera. Previous bindings are dropped.</summary>
        public void Bind(UnitSelectionManager selection, CombatSystem combat, IsometricCameraRig cameraRig)
        {
            Unbind();

            _selection = selection;
            _combat = combat;
            _cameraRig = cameraRig;

            if (_selection != null) _selection.OnSelectionChanged += Refresh;
            Refresh(_selection != null ? _selection.Selection : null);
        }

        public void Unbind()
        {
            if (_selection != null) _selection.OnSelectionChanged -= Refresh;
            _selection = null;
            _combat = null;
            _cameraRig = null;
        }

        /// <summary>Render the current selection: count, type name and aggregate health.</summary>
        public void Refresh(IReadOnlyList<UnitController> units)
        {
            int count = 0;
            int health = 0;
            int maxHealth = 0;
            string firstName = null;
            string firstIconId = null;

            if (units != null)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    UnitController unit = units[i];
                    if (unit == null || unit.IsDead) continue;

                    count++;
                    health += unit.Health;
                    maxHealth += unit.MaxHealth;
                    if (firstName == null && unit.Definition != null)
                    {
                        firstName = unit.Definition.displayName;
                        firstIconId = unit.Definition.id;
                    }
                }
            }

            if (portraitIcon != null)
            {
                portraitIcon.sprite = count == 0 ? null : UiFactory.UiSprite(UiFactory.IconForId(firstIconId));
                portraitIcon.enabled = portraitIcon.sprite != null;
            }

            if (summaryLabel != null)
            {
                summaryLabel.text = count == 0
                    ? "No units selected"
                    : count == 1
                        ? firstName ?? "Unit"
                        : $"{firstName ?? "Units"} +{count - 1}  ({count} selected)";
            }

            if (detailLabel != null)
            {
                detailLabel.text = count == 0 ? "Tap a unit, or drag a box" : $"Health {health}/{maxHealth}";
            }

            // Move/Attack/Stop are meaningless with nothing selected; leaving them lit would invite
            // taps that do nothing at all.
            bool hasSelection = count > 0;
            UiFactory.SetInteractable(moveButton, hasSelection);
            UiFactory.SetInteractable(stopButton, hasSelection);
            UiFactory.SetInteractable(attackButton, hasSelection && _combat != null);
        }

        /// <summary>Move the selection to the camera rig's focus point. Returns how many units were ordered.</summary>
        public int MoveSelectionToFocus()
        {
            if (_selection == null || _cameraRig == null) return 0;
            return _selection.CommandMove(_cameraRig.FocusPoint);
        }

        /// <summary>Order every selected combat unit onto the nearest living enemy. Returns units ordered.</summary>
        public int AttackNearestEnemy()
        {
            if (_selection == null || _combat == null) return 0;

            Vector3 centre = SelectionCentre();
            IDamageable target = _combat.FindNearestEnemy(Team.Player, centre, attackSearchRadius);
            if (target == null) return 0;

            int ordered = 0;
            IReadOnlyList<UnitController> units = _selection.Selection;
            for (int i = 0; i < units.Count; i++)
            {
                UnitController unit = units[i];
                if (unit == null || unit.IsDead || !unit.IsCombatUnit) continue;
                unit.Attack(target);
                ordered++;
            }

            return ordered;
        }

        /// <summary>Halt every selected unit. Returns how many were stopped.</summary>
        public int StopSelection()
        {
            if (_selection == null) return 0;

            int stopped = 0;
            IReadOnlyList<UnitController> units = _selection.Selection;
            for (int i = 0; i < units.Count; i++)
            {
                UnitController unit = units[i];
                if (unit == null || unit.IsDead) continue;
                unit.Stop();
                stopped++;
            }

            return stopped;
        }

        public int SelectAllCombatUnits() => _selection != null ? _selection.SelectAllCombatUnits() : 0;

        public bool CenterOnTownHall() => _selection != null && _selection.CenterOnTownHall();

        private Vector3 SelectionCentre()
        {
            if (_selection == null || _selection.SelectionCount == 0) return Vector3.zero;

            Vector3 sum = Vector3.zero;
            int count = 0;
            IReadOnlyList<UnitController> units = _selection.Selection;
            for (int i = 0; i < units.Count; i++)
            {
                UnitController unit = units[i];
                if (unit == null || unit.IsDead) continue;
                sum += unit.WorldPosition;
                count++;
            }

            return count == 0 ? Vector3.zero : sum / count;
        }

        private void OnDestroy() => Unbind();
    }
}

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace LifeOfEmpires.UI
{
    /// <summary>
    /// The battle HUD: one canvas that owns the resource bar, the selection panel, the build menu, the
    /// production panel and the results card.
    ///
    /// The canvas is authored in the scene by <c>ProjectBootstrap</c> and rebuilt here when it is
    /// absent, so the same code produces the shipped interface and the one an EditMode test inspects.
    /// Nothing in the HUD owns game state: every control calls into an existing system — the placement
    /// system, the production slot, the objective manager, the battle manager — which is what keeps a
    /// tap and a button from ever doing different things.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class BattleHud : MonoBehaviour
    {
        [Tooltip("Pixel radius within which a tap counts as selecting a building.")]
        [Min(8f)] public float tapRadiusPixels = 72f;

        public Canvas canvas;
        public CanvasScaler scaler;
        public GraphicRaycaster raycaster;
        public SafeAreaFitter safeArea;
        public UIResourcesBar resourcesBar;
        public UISelectionPanel selectionPanel;
        public UIBuildMenu buildMenu;
        public UIProductionPanel productionPanel;
        public UIResultsPanel resultsPanel;
        public Button pauseButton;
        public Text hintLabel;
        public Text cueLabel;

        private UnitSelectionManager _selection;
        private BuildingPlacementSystem _placement;
        private bool _pointerDown;
        private bool _pointerStartedOverHud;
        private Vector2 _pointerDownPosition;
        private float _pointerMaxDrift;

        /// <summary>The player building whose production panel is open, or null.</summary>
        public BuildingController SelectedBuilding { get; private set; }

        private void Awake()
        {
            canvas = GetComponent<Canvas>();
        }

        private void Start()
        {
            Bind();
        }

        /// <summary>
        /// Create the whole HUD hierarchy under this object. Idempotent — the scene already carries the
        /// result of a previous build.
        /// </summary>
        public void Build(GameDatabase database)
        {
            if (resourcesBar != null) return;

            EnsureCanvas();

            RectTransform root = UiFactory.Rect("SafeArea", transform);
            UiFactory.Stretch(root);
            safeArea = root.gameObject.AddComponent<SafeAreaFitter>();

            // First-run guidance (spec §28): one hint line and one control cue, sitting in the free
            // band below the resource bar and left of the pause button. They are hints, not gates —
            // nothing waits on the player reading them.
            hintLabel = UiFactory.Label("ObjectiveHint", root, BuildObjectiveText(), 30, TextAnchor.UpperLeft, UiFactory.TextColor);
            UiFactory.Place(hintLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -104f), new Vector2(-236f, 56f));

            cueLabel = UiFactory.Label("GatherCue", root, "Tap a worker, then tap a tree or farm to gather.", 26, TextAnchor.UpperLeft, UiFactory.AccentColor);
            UiFactory.Place(cueLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -162f), new Vector2(-236f, 48f));

            resourcesBar = root.gameObject.AddComponent<UIResourcesBar>();
            resourcesBar.Build(root);

            selectionPanel = root.gameObject.AddComponent<UISelectionPanel>();
            selectionPanel.Build(root);

            buildMenu = root.gameObject.AddComponent<UIBuildMenu>();
            buildMenu.Build(root, database);

            productionPanel = root.gameObject.AddComponent<UIProductionPanel>();
            productionPanel.Build(root, database);

            resultsPanel = root.gameObject.AddComponent<UIResultsPanel>();
            resultsPanel.Build(root);

            pauseButton = UiFactory.Button("PauseButton", root, "Pause", 28, TogglePause, "icon_pause");
            UiFactory.Place(pauseButton.GetComponent<RectTransform>(),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-16f, -116f), new Vector2(180f, 84f));
        }

        /// <summary>Wire the panels to the live battle. Safe to call repeatedly.</summary>
        public void Bind()
        {
            BattleContext context = BattleContext.Current;

            if (resourcesBar == null && context != null) Build(context.Database);
            if (resourcesBar == null) return;

            // At first Build the context may not exist yet, so the hint is refreshed here too.
            if (hintLabel != null) hintLabel.text = BuildObjectiveText();

            resourcesBar.Bind(context);

            UnitSelectionManager selection = null;
            CombatSystem combat = null;
            IsometricCameraRig cameraRig = null;
            BattleManager battle = null;
            BuildingPlacementSystem placement = null;

            if (context != null) combat = context.Combat;

            ServiceLocator.TryGet(out selection);
            ServiceLocator.TryGet(out cameraRig);
            ServiceLocator.TryGet(out battle);
            ServiceLocator.TryGet(out placement);

            selectionPanel.Bind(selection, combat, cameraRig);
            buildMenu.Bind(placement, context != null ? context.PlayerResources : null);
            productionPanel.Bind(context != null ? context.PlayerResources : null);
            resultsPanel.Bind(battle != null ? battle.objectives : null, battle, context != null ? context.Database : null);

            _selection = selection;
            _placement = placement;
        }

        /// <summary>
        /// The objective line shown at battle start. It reuses the mission briefing the player already
        /// read, so the objective is never worded twice differently (spec §28 "teach through actions").
        /// </summary>
        private static string BuildObjectiveText()
        {
            MissionDefinition mission = BattleContext.Current != null ? BattleContext.Current.Mission : null;
            if (mission == null || string.IsNullOrEmpty(mission.briefing)) return string.Empty;
            return $"{mission.title}: {mission.briefing}";
        }

        /// <summary>Author the one EventSystem the HUD needs. Never adds a second input module.</summary>
        public static void EnsureEventSystem()
        {
            EventSystem existing = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (existing == null)
            {
                var go = new GameObject("EventSystem");
                existing = go.AddComponent<EventSystem>();
            }

#if ENABLE_INPUT_SYSTEM
            // Active Input Handling is "Input System Package" only, so the uGUI module that reads
            // legacy Input would receive nothing. The Input System module assigns its default actions
            // in OnEnable, which is why no .inputactions asset is authored.
            if (existing.GetComponent<InputSystemUIInputModule>() == null)
            {
                existing.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            StandaloneInputModule legacy = existing.GetComponent<StandaloneInputModule>();
            if (legacy != null) DestroyImmediate(legacy);
#endif
        }

        public void TogglePause()
        {
            if (ServiceLocator.TryGet(out BattleManager battle) && battle != null) battle.TogglePause();
        }

        private void EnsureCanvas()
        {
            if (canvas == null) canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            if (scaler == null) scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(UiFactory.ReferenceWidth, UiFactory.ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            // Portrait: the width is what must fit, so the layout is matched on width alone — verifying
            // the real device layout is still outstanding (see DEVELOPMENT_STATUS.md).
            scaler.matchWidthOrHeight = 1f;

            if (raycaster == null) raycaster = GetComponent<GraphicRaycaster>();
            if (raycaster == null) raycaster = gameObject.AddComponent<GraphicRaycaster>();
        }

        private void Update()
        {
            PollPointer();
            RefreshPauseLabel();
        }

        private void RefreshPauseLabel()
        {
            if (pauseButton == null) return;
            if (!ServiceLocator.TryGet(out BattleManager battle) || battle == null) return;

            Text label = pauseButton.GetComponentInChildren<Text>();
            if (label != null) label.text = battle.IsPaused ? "Resume" : "Pause";
        }

        /// <summary>Open or close the production panel for a building. Null closes it.</summary>
        public void ShowProductionFor(BuildingController building)
        {
            SelectedBuilding = building;
            if (productionPanel != null) productionPanel.Show(building);
        }

        // -----------------------------------------------------------------------------------------
        // Tap-to-select a player building
        // -----------------------------------------------------------------------------------------

        private void PollPointer()
        {
            bool down = false;
            Vector2 position = default;

#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                down = mouse.leftButton.isPressed;
                position = mouse.position.ReadValue();
            }

            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.isPressed)
            {
                down = true;
                position = touchscreen.primaryTouch.position.ReadValue();
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0)
            {
                down = true;
                position = Input.GetTouch(0).position;
            }
            else if (Input.GetMouseButton(0))
            {
                down = true;
                position = Input.mousePosition;
            }
#endif

            if (down)
            {
                if (!_pointerDown)
                {
                    _pointerDown = true;
                    _pointerDownPosition = position;
                    _pointerMaxDrift = 0f;
                    _pointerStartedOverHud = IsPointerOverHud();
                }
                else
                {
                    _pointerMaxDrift = Mathf.Max(_pointerMaxDrift, Vector2.Distance(position, _pointerDownPosition));
                }

                return;
            }

            if (!_pointerDown) return;

            _pointerDown = false;
            bool wasTap = !_pointerStartedOverHud && _pointerMaxDrift <= tapRadiusPixels * 0.3f;
            if (wasTap) HandleWorldTap(_pointerDownPosition);
        }

        private static bool IsPointerOverHud()
        {
            EventSystem eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }

        private void HandleWorldTap(Vector2 screenPoint)
        {
            // While a building is being placed the tap belongs to the ghost: the input controller
            // confirms it, and opening a panel on the same tap would be a second meaning for one finger.
            if (_placement != null && _placement.IsPlacing) return;

            Camera camera = ResolveCamera();
            if (camera == null) return;

            float radius = _selection != null ? _selection.tapScreenRadius : tapRadiusPixels;

            // The panel follows the last building tapped, whatever the army is doing. It is never
            // closed by a unit selection: an army being selected must not lock the train buttons away,
            // and the input controller leaves a tap on one of our own buildings to this panel rather
            // than turning it into a move order.
            ShowProductionFor(BattleContext.FindPlayerBuildingNearScreenPoint(camera, screenPoint, radius));
        }

        private Camera ResolveCamera()
        {
            if (ServiceLocator.TryGet(out IsometricCameraRig rig) && rig != null) return rig.Camera;
            return Camera.main;
        }

        private void OnDestroy()
        {
            if (resourcesBar != null) resourcesBar.Unbind();
            if (selectionPanel != null) selectionPanel.Unbind();
            if (buildMenu != null) buildMenu.Unbind();
            if (productionPanel != null) productionPanel.Unbind();
            if (resultsPanel != null) resultsPanel.Unbind();
        }
    }
}

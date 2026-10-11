using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LifeOfEmpires.UI
{
    /// <summary>
    /// The campaign map: one mission plate per authored mission, laid out by
    /// <see cref="CampaignMapLayout"/> over a grass surface — territory bands in conquest order,
    /// missions serpentined inside each band, a wooden ribbon tracing the route. Stars, locks and
    /// conquest state all come from <see cref="CampaignManager"/> — the screen never re-implements
    /// the unlock rule or star scoring, it only positions what the rules already decided.
    ///
    /// Locked missions are shown, not hidden: spec §11 requires the map to represent the kingdoms
    /// still ahead. Selecting a plate only shows its briefing; playing is a separate, explicit
    /// button, so a mis-tap cannot drop the player into a battle they cannot win.
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

        /// <summary>Every repaintable visual one node owns, so Refresh walks one list per row.</summary>
        private sealed class NodeVisual
        {
            public Image Plate;
            public Image Emblem;
            public readonly Image[] Stars = new Image[3];
        }

        // Node plates and territory headers repaint in place on Refresh — rebuilding the whole map
        // for a star change would flicker and is never necessary. Headers stay a separate list:
        // they are per-band, not per-row.
        private readonly List<NodeVisual> _visuals = new List<NodeVisual>();
        private readonly List<Text> _headers = new List<Text>();

        private RectTransform _mapContent;
        private List<MapNode> _nodes;
        private float _mapWidth = FallbackMapWidth;

        // The scroll viewport's effective width in reference pixels (1080 minus its insets); the
        // real rect width is preferred when layout has already run.
        private const float FallbackMapWidth = 1032f;
        private const float NodeSize = 300f;
        private const float PlateSize = 170f;
        private const float PlateCentre = 110f;
        private const float TitleBand = 64f;
        private const float StarRowHeight = 44f;
        private const float StatusBand = 40f;
        private const float NodeTail = 60f;

        private static readonly Color UnlockedTint = new Color(1f, 1f, 1f);
        private static readonly Color CompletedTint = new Color(0.72f, 0.92f, 0.66f);
        private static readonly Color LockedTint = new Color(0.52f, 0.52f, 0.55f);
        private static readonly Color RibbonTint = new Color(0.55f, 0.38f, 0.22f, 0.85f);

        public void Build(GameDatabase database)
        {
            this.database = database;
            if (this.database == null) return;

            if (menu == null) menu = gameObject.AddComponent<MenuCanvas>();
            menu.Build("Life of Empires", "Campaign — choose a mission.");

            BuildMap();
            BuildControls();
            Refresh();
            BuildTitleGate();
            SceneTransition.FadeInOnLoad();
        }

        /// <summary>Once per launch: a full-screen gate so the app opens on a title, not a list. Public so tests can reset it.</summary>
        public static bool s_titleShown;
        private GameObject _titleGate;

        private void BuildTitleGate()
        {
            if (s_titleShown || transform == null) return;

            RectTransform gate = UiFactory.Rect("TitleGate", transform);
            UiFactory.Stretch(gate);
            _titleGate = gate.gameObject;

            // The dim backdrop must also eat taps so the map behind stays inert until Begin.
            Image dim = UiFactory.Panel("Dim", gate, new Color(0f, 0f, 0f, 0.66f));
            dim.raycastTarget = true;
            UiFactory.Stretch(dim.rectTransform);

            Image crown = UiFactory.Icon("Crown", gate, "icon_crown");
            UiFactory.Place(crown.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 300f), new Vector2(190f, 190f));

            Text title = UiFactory.Label("GateTitle", gate, "Life of Empires", 72, TextAnchor.MiddleCenter, UiFactory.AccentColor);
            title.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            UiFactory.Place(title.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 120f), new Vector2(920f, 110f));

            Text tagline = UiFactory.Label("GateTagline", gate, "Forge your kingdom.", 30, TextAnchor.MiddleCenter, UiFactory.TextColor);
            UiFactory.Place(tagline.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 40f), new Vector2(760f, 60f));

            Button begin = UiFactory.Button("BeginButton", gate, "Begin Conquest", 40,
                () => { s_titleShown = true; if (_titleGate != null) _titleGate.SetActive(false); }, "icon_scroll");
            UiFactory.Place(begin.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -170f), new Vector2(560f, 130f));
        }

        /// <summary>Re-read the save and repaint every plate. Called from Start and whenever the map is shown.</summary>
        public void Refresh()
        {
            if (database == null || state.Rows == null) return;

            // The viewport width is only real after layout has run — re-measure before repainting
            // so the content rect never carries a stale height into a resized scroll surface.
            _nodes = CampaignMapLayout.Build(database, SaveSystem.ReadFromDisk());
            ApplyContentHeight();

            SaveData save = SaveSystem.ReadFromDisk();
            List<MapNode> nodes = _nodes;

            for (int i = 0; i < state.Rows.Count; i++)
            {
                MissionRow row = state.Rows[i];
                if (row.Mission == null || i >= nodes.Count) continue;
                MapNode node = nodes[i];

                bool unlocked = !node.Locked;
                UiFactory.SetInteractable(row.Button, unlocked);
                if (row.TitleLabel != null) row.TitleLabel.text = row.Mission.title;
                if (row.StarsLabel != null) row.StarsLabel.text = unlocked ? CampaignManager.StarLabel(save, row.Mission) : "Locked";
                if (row.LockIcon != null) row.LockIcon.enabled = node.Locked && row.LockIcon.sprite != null;

                if (i < _visuals.Count)
                {
                    NodeVisual visual = _visuals[i];
                    if (visual.Plate != null)
                        visual.Plate.color = node.Locked ? LockedTint : node.Completed ? CompletedTint : UnlockedTint;
                    if (visual.Emblem != null)
                        visual.Emblem.enabled = visual.Emblem.sprite != null && !node.Locked;

                    // Star icons repaint against the node's own record — one icon per earned star.
                    for (int s = 0; s < visual.Stars.Length; s++)
                    {
                        if (visual.Stars[s] != null)
                            visual.Stars[s].enabled = visual.Stars[s].sprite != null && s < node.Stars;
                    }
                }
            }

            // Territory headers repaint against the recomputed progress in the same pass.
            RepaintHeaders(save, nodes);
        }

        /// <summary>Select a plate by index. A locked plate can still be selected so its briefing can be read.</summary>
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
            else
            {
                Refresh();
                // The gate is baked into the scene — a scene reload (back from battle) must
                // not resurrect it once the launch's title has already been dismissed.
                Transform gate = transform.Find("TitleGate");
                if (gate != null && s_titleShown) gate.gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (state.Rows != null && state.Rows.Count > 0) Refresh();
        }

        private void BuildMap()
        {
            state.Rows = new List<MissionRow>();
            state.SelectedIndex = -1;
            _visuals.Clear();
            _headers.Clear();

            SaveData save = SaveSystem.ReadFromDisk();
            _nodes = CampaignMapLayout.Build(database, save);

            // A map reads better with a little spring at the edges than with a hard stop.
            if (menu.listRoot != null && menu.listRoot.parent != null)
            {
                ScrollRect scroll = menu.listRoot.parent.GetComponent<ScrollRect>();
                if (scroll != null) scroll.movementType = ScrollRect.MovementType.Elastic;
            }

            // The scroll content keeps its "List" name; the map surface inside it is "mapContent",
            // a top-anchored rect sized to the full layout — scrolling is the viewport's job.
            _mapContent = UiFactory.Rect("mapContent", menu.listRoot);
            _mapContent.anchorMin = new Vector2(0f, 1f);
            _mapContent.anchorMax = new Vector2(1f, 1f);
            _mapContent.pivot = new Vector2(0.5f, 1f);
            _mapContent.anchoredPosition = Vector2.zero;
            _mapContent.sizeDelta = Vector2.zero;

            // Size both rects from the real viewport width before any child lands inside them.
            ApplyContentHeight();
            RectTransform mapContent = _mapContent;
            // Positions bake against the reference width: nodes anchor by fraction horizontally and
            // by pixel vertically, so the layout is identical at any runtime viewport width — the
            // batch editor's canvas measures far wider than a phone and must not distort the bake.
            float mapWidth = FallbackMapWidth;

            // tex_grass imports as a Default texture (the material path needs that), not a sprite —
            // so the terrain backdrop goes through RawImage rather than the sprite loader.
            RectTransform backdropRect = UiFactory.Rect("mapBackdrop", mapContent);
            var backdrop = backdropRect.gameObject.AddComponent<RawImage>();
            backdrop.texture = Resources.Load<Texture2D>("UI/tex_grass");
            backdrop.color = new Color(0.9f, 0.95f, 0.85f, backdrop.texture != null ? 0.85f : 0f);
            backdrop.raycastTarget = false;
            UiFactory.Stretch(backdropRect);

            Image terrainScrim = UiFactory.Panel("TerrainScrim", mapContent, new Color(0f, 0f, 0f, 0.14f));
            UiFactory.Stretch(terrainScrim.rectTransform);

            // Territory by territory: header at the band top, the ribbon between the two columns,
            // then every plate anchored on its serpentine point.
            int bandStart = 0;
            while (bandStart < _nodes.Count)
            {
                int bandEnd = bandStart;
                string territory = _nodes[bandStart].Territory;
                while (bandEnd < _nodes.Count && _nodes[bandEnd].Territory == territory) bandEnd++;

                var bandNodes = _nodes.GetRange(bandStart, bandEnd - bandStart);
                BuildBand(mapContent, territory, bandNodes, mapWidth, save);
                bandStart = bandEnd;
            }
        }

        /// <summary>
        /// Measure the viewport's laid-out width and size the scroll content — and the map surface
        /// inside it — to the layout's full height. Called from BuildMap and every Refresh so a
        /// repainted map can never carry a stale or fallback-measured height. Before layout runs
        /// (batch authoring, EditMode) the viewport rect is empty and the reference-width fallback
        /// stands in.
        /// </summary>
        private void ApplyContentHeight()
        {
            if (menu == null || menu.listRoot == null) return;

            Canvas.ForceUpdateCanvases();
            RectTransform viewport = menu.listRoot.parent as RectTransform;
            float measured = viewport != null ? viewport.rect.width : 0f;
            // The scroll extent is the one place the real width is wanted — never let a measured
            // width shrink it below the reference bake, which is what the node positions assume.
            _mapWidth = Mathf.Max(measured, FallbackMapWidth);

            float contentHeight = Mathf.Max(400f, CampaignMapLayout.ContentHeight(_nodes) * _mapWidth + NodeTail);
            menu.SetContentHeight(contentHeight);
            if (_mapContent != null) _mapContent.sizeDelta = new Vector2(0f, contentHeight);
        }

        private void BuildBand(RectTransform mapContent, string territory, List<MapNode> bandNodes, float mapWidth, SaveData save)
        {
            CampaignMapLayout.ExtentOf(bandNodes, out float minY, out float maxY);
            float bandTop = Mathf.Max(0.02f, minY - CampaignMapLayout.HeaderGap * 0.85f);

            // Territory header: name plus its conquest tally on a wooden banner above the band.
            (int stars, int configured, bool conquered) = CampaignManager.TerritoryProgress(database, save, territory);
            string headerText = conquered
                ? $"{territory}  {stars}/{configured}  —  CONQUERED"
                : $"{territory}  {stars}/{configured}";
            Image banner = UiFactory.Icon($"Banner_{territory}", mapContent, "btn_wood");
            banner.raycastTarget = false;
            banner.preserveAspect = false;
            UiFactory.Place(banner.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -bandTop * mapWidth), new Vector2(660f, 96f));
            Text header = UiFactory.Label($"Territory_{territory}", banner.rectTransform, headerText, 36, TextAnchor.MiddleCenter, UiFactory.AccentColor);
            header.raycastTarget = false;
            header.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.85f);
            UiFactory.Stretch(header.rectTransform, left: 20f, right: 20f);
            _headers.Add(header);

            // The ribbon: a wooden route line running between the band's first and last plates,
            // with a stub reaching out to each plate so the map reads as a path, not a list.
            // It starts at the first plate's centre — entering the header zone crossed the banner.
            if (bandNodes.Count > 0)
            {
                Image ribbon = UiFactory.Panel($"Ribbon_{territory}", mapContent, RibbonTint);
                float ribbonTop = minY * mapWidth + PlateCentre;
                float ribbonBottom = maxY * mapWidth + PlateCentre;
                UiFactory.Place(ribbon.rectTransform,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -ribbonTop), new Vector2(30f, ribbonBottom - ribbonTop));
            }

            foreach (MapNode node in bandNodes)
            {
                Vector2 point = CampaignMapLayout.PositionOf(node, mapWidth);

                // The horizontal stub from the route to the plate — its direction follows the column.
                if (Mathf.Abs(node.Anchor.x - 0.5f) > 0.01f)
                {
                    Image stub = UiFactory.Panel($"Stub_{node.Mission.missionId}", mapContent, RibbonTint);
                    float left = Mathf.Min(node.Anchor.x, 0.5f);
                    float right = Mathf.Max(node.Anchor.x, 0.5f);
                    stub.rectTransform.anchorMin = new Vector2(left, 1f);
                    stub.rectTransform.anchorMax = new Vector2(right, 1f);
                    stub.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    stub.rectTransform.anchoredPosition = new Vector2(0f, point.y - PlateCentre);
                    stub.rectTransform.sizeDelta = new Vector2(0f, 18f);
                }

                BuildNode(mapContent, node, point);
            }
        }

        private void BuildNode(RectTransform mapContent, MapNode node, Vector2 point)
        {
            // The node GameObject carries the real Button — row lookups and interactable counts
            // bind to it directly. Its top edge sits on the anchor point and hangs below it.
            RectTransform rowRect = UiFactory.Rect($"MissionRow_{node.Mission.missionId}", mapContent);
            // x rides an anchor fraction of the map width — a pixel x would bake in whatever width
            // the canvas happened to measure at author time and clip off-screen at runtime.
            UiFactory.Place(rowRect,
                new Vector2(node.Anchor.x, 1f), new Vector2(node.Anchor.x, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, point.y), new Vector2(NodeSize, NodeSize + NodeTail));

            var button = rowRect.gameObject.AddComponent<Button>();
            // The Button's own GameObject must own NO Graphic: Selectable.Awake() silently
            // assigns m_TargetGraphic = GetComponent<Graphic>() whenever the field is null, so a
            // same-object face resurrects at runtime as a tintable plaque behind every node
            // (that was the blue rect). A transparent child catches taps instead — uGUI events
            // bubble up to the Button, and SetInteractable's tint branch stays dead forever.
            Image hitCatcher = UiFactory.Panel("HitCatcher", rowRect, new Color(0f, 0f, 0f, 0f));
            hitCatcher.raycastTarget = true;
            UiFactory.Stretch(hitCatcher.rectTransform);

            int index = state.Rows.Count;
            button.onClick.AddListener(() => SelectRow(index));

            var visual = new NodeVisual();

            // A soft drop shadow gives the plate depth against the grass.
            Image plateShadow = UiFactory.Icon("PlateShadow", rowRect, "panel_parchment");
            plateShadow.color = new Color(0f, 0f, 0f, 0.35f);
            plateShadow.raycastTarget = false;
            UiFactory.Place(plateShadow.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(6f, -PlateCentre + 6f), new Vector2(PlateSize, PlateSize));

            // All children hang below the node's top edge, stacked without overlap:
            // plate, title banner, stars, then the status line.
            Image plate = UiFactory.Icon("Plate", rowRect, "panel_parchment");
            plate.raycastTarget = false;
            UiFactory.Place(plate.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -PlateCentre), new Vector2(PlateSize, PlateSize));
            visual.Plate = plate;

            Image emblem = UiFactory.Icon("Emblem", rowRect, "icon_scroll");
            emblem.raycastTarget = false;
            UiFactory.Place(emblem.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -PlateCentre), new Vector2(92f, 92f));
            visual.Emblem = emblem;

            // The title rides on a wooden banner below the plate — never on top of it.
            Image titleBanner = UiFactory.Icon("TitleBanner", rowRect, "btn_wood");
            titleBanner.raycastTarget = false;
            titleBanner.preserveAspect = false;
            UiFactory.Place(titleBanner.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(PlateCentre + PlateSize * 0.5f + 14f)), new Vector2(NodeSize + 60f, TitleBand));

            Text title = UiFactory.Label("Title", rowRect, node.Mission.title, 26, TextAnchor.MiddleCenter, UiFactory.AccentColor);
            title.raycastTarget = false;
            title.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.85f);
            UiFactory.Place(title.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -(PlateCentre + PlateSize * 0.5f + 14f + TitleBand * 0.5f)),
                new Vector2(NodeSize + 40f, TitleBand));

            float starsTop = PlateCentre + PlateSize * 0.5f + 22f + TitleBand;
            for (int s = 0; s < 3; s++)
            {
                Image star = UiFactory.Icon($"Star{s}", rowRect, "icon_star");
                star.raycastTarget = false;
                UiFactory.Place(star.rectTransform,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2((s - 1) * 46f, -starsTop),
                    new Vector2(40f, 40f));
                visual.Stars[s] = star;
            }

            Text stars = UiFactory.Label("Stars", rowRect, string.Empty, 24, TextAnchor.MiddleCenter, UiFactory.AccentColor);
            stars.raycastTarget = false;
            stars.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.85f);
            UiFactory.Place(stars.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(starsTop + StarRowHeight + 4f)),
                new Vector2(NodeSize + 60f, StatusBand));

            Image lockIcon = UiFactory.Icon("Lock", rowRect, "icon_lock");
            lockIcon.raycastTarget = false;
            UiFactory.Place(lockIcon.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -PlateCentre), new Vector2(64f, 64f));
            lockIcon.color = new Color(1f, 1f, 1f, 0.95f);

            _visuals.Add(visual);
            state.Rows.Add(new MissionRow
            {
                Mission = node.Mission,
                Button = button,
                TitleLabel = title,
                StarsLabel = stars,
                LockIcon = lockIcon
            });
        }

        private void RepaintHeaders(SaveData save, List<MapNode> nodes)
        {
            // Headers were authored band by band in node order; the same order repaints them.
            int bandStart = 0;
            int headerIndex = 0;
            while (bandStart < nodes.Count && headerIndex < _headers.Count)
            {
                int bandEnd = bandStart;
                string territory = nodes[bandStart].Territory;
                while (bandEnd < nodes.Count && nodes[bandEnd].Territory == territory) bandEnd++;

                (int stars, int configured, bool conquered) = CampaignManager.TerritoryProgress(database, save, territory);
                _headers[headerIndex].text = conquered
                    ? $"{territory}  {stars}/{configured}  —  CONQUERED"
                    : $"{territory}  {stars}/{configured}";

                headerIndex++;
                bandStart = bandEnd;
            }
        }

        private void BuildControls()
        {
            // Play and the nav row sit on the fixed footer: the map scrolls beneath them and a tap
            // on them can never be buried under a late plate.
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

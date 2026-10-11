using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LifeOfEmpires.UI
{
    /// <summary>
    /// The shared shell every menu screen sits on: one canvas, one safe area, a title and a subtitle,
    /// and a list root that rows are placed into. It mirrors <see cref="BattleHud"/>'s canvas exactly
    /// — same render mode, sorting order, scaler and builtin font — so a menu and the battle HUD look
    /// like one product and there is only one place that knows how a screen is framed.
    ///
    /// It owns no game state: screens read the save and call into the runtime systems themselves.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class MenuCanvas : MonoBehaviour
    {
        public Canvas canvas;
        public CanvasScaler scaler;
        public GraphicRaycaster raycaster;
        public SafeAreaFitter safeArea;
        public Text titleLabel;
        public Text subtitleLabel;
        public RectTransform listRoot;

        /// <summary>Create the shell. Idempotent: a rebuild clears the previous shell rather than stacking on it.</summary>
        public void Build(string title, string subtitle)
        {
            EnsureCanvas();

            // The bootstrap authors one shell into the scene; a runtime rebuild (or a test building
            // over a scene) must clear it first, or every rebuild would stack a second copy on top.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                DestroyChild(transform.GetChild(i).gameObject);
            }

            RectTransform safeRoot = UiFactory.Rect("SafeArea", transform);
            UiFactory.Stretch(safeRoot);
            safeArea = safeRoot.gameObject.AddComponent<SafeAreaFitter>();

            titleLabel = UiFactory.Label("Title", safeRoot, title, 52, TextAnchor.MiddleLeft, UiFactory.TextColor);
            UiFactory.Place(titleLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -32f), new Vector2(-48f, 90f));

            subtitleLabel = UiFactory.Label("Subtitle", safeRoot, subtitle, 32, TextAnchor.UpperLeft, UiFactory.AccentColor);
            UiFactory.Place(subtitleLabel.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -126f), new Vector2(-48f, 110f));

            listRoot = UiFactory.Rect("List", safeRoot);
            UiFactory.Stretch(listRoot, left: 24f, bottom: 24f, right: 24f, top: 260f);
        }

        /// <summary>Author the one EventSystem the game is allowed. Delegates to the HUD's implementation.</summary>
        public static void EnsureEventSystem() => BattleHud.EnsureEventSystem();

        /// <summary>A fixed-height row inside <see cref="listRoot"/>, sized but not yet placed.</summary>
        public RectTransform NewRow(string name) => UiFactory.Rect(name, listRoot);

        /// <summary>
        /// Load a menu scene. In the editor — where EditMode tests build these screens and press their
        /// buttons without a player loop — the load is skipped so the screen's own logic stays
        /// testable; a shipped build always loads.
        /// </summary>
        public static void LoadScene(string sceneName)
        {
            if (Application.isPlaying) SceneManager.LoadScene(sceneName);
        }

        private static void DestroyChild(GameObject child)
        {
            if (Application.isPlaying) Object.Destroy(child);
            else Object.DestroyImmediate(child);
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
            scaler.matchWidthOrHeight = 1f;

            if (raycaster == null) raycaster = GetComponent<GraphicRaycaster>();
            if (raycaster == null) raycaster = gameObject.AddComponent<GraphicRaycaster>();
        }
    }
}

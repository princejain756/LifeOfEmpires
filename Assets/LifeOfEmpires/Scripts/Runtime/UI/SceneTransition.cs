using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LifeOfEmpires.UI
{
    /// <summary>
    /// The single scene-load choke point: every load — menu nav, Play, results Return, bootstrap —
    /// routes through <see cref="Go"/> so a fade to black can run in front of all eight call sites
    /// and a double-tap can never queue two loads.
    ///
    /// The fade runs on unscaled time: a paused battle (<c>Time.timeScale = 0</c>) must still be able
    /// to fade out. Outside play mode nothing changes — the action still runs, so EditMode tests and
    /// the editor bootstrap see exactly the old behaviour.
    /// </summary>
    public static class SceneTransition
    {
        public static float FadeOutSeconds = 0.22f;
        public static float FadeInSeconds = 0.28f;

        /// <summary>True while a fade or the load inside it is in flight.</summary>
        public static bool IsTransitioning { get; private set; }

        private static GameObject _host;
        private static GameObject _overlay;
        private static Image _veil;
        private static bool _fadingIn;

        /// <summary>
        /// Run <paramref name="onReady"/> (a scene load by default) behind a fade to black.
        /// A second request while one is in flight is dropped — two queued loads would land on
        /// whichever scene won the race.
        /// </summary>
        public static void Go(string sceneName, Action onReady = null)
        {
            onReady ??= () => SceneManager.LoadScene(sceneName);

            if (!Application.isPlaying)
            {
                onReady();
                return;
            }

            if (IsTransitioning)
            {
                Debug.LogWarning($"SceneTransition: a load is already in flight; dropping '{sceneName}'.");
                return;
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("SceneTransition: empty scene name; nothing loaded.");
                return;
            }

            IsTransitioning = true;
            _fadingIn = false;
            EnsureOverlay();
            EnsureHost().GetComponent<TransitionHost>().Run(FadeOutThenLoad(onReady));
        }

        /// <summary>
        /// Begin the fade-in early. Menu screens call this from Build so the veil lifts the moment
        /// their content exists rather than one frame later. Safe at any time — it only acts while a
        /// transition is actually in flight.
        /// </summary>
        public static void FadeInOnLoad()
        {
            if (!Application.isPlaying || !IsTransitioning || _fadingIn || _veil == null) return;
            _fadingIn = true;
            EnsureHost().GetComponent<TransitionHost>().Run(FadeIn());
        }

        private static IEnumerator FadeOutThenLoad(Action onReady)
        {
            yield return FadeVeil(1f, FadeOutSeconds);

            try
            {
                onReady();
            }
            catch (Exception error)
            {
                Debug.LogError($"SceneTransition: load failed — {error.Message}");
                Teardown();
                yield break;
            }

            // The new scene activates on the next frame; FadeInOnLoad may already have started the
            // fade-in from a menu Build, in which case this is a no-op.
            yield return null;
            if (!_fadingIn)
            {
                _fadingIn = true;
                yield return FadeIn();
            }
            else
            {
                // FadeInOnLoad is mid-fade on the host — wait for it to finish before teardown.
                while (IsTransitioning) yield return null;
            }
        }

        private static IEnumerator FadeIn()
        {
            yield return FadeVeil(0f, FadeInSeconds);
            Teardown();
        }

        private static IEnumerator FadeVeil(float target, float seconds)
        {
            if (_veil == null) yield break;

            float start = _veil.color.a;
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                Color color = _veil.color;
                color.a = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / seconds));
                _veil.color = color;
                yield return null;
            }

            Color final = _veil.color;
            final.a = target;
            _veil.color = final;
        }

        private static void EnsureOverlay()
        {
            if (_overlay == null)
            {
                _overlay = new GameObject("SceneTransitionOverlay", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                var canvas = _overlay.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32760;
                UnityEngine.Object.DontDestroyOnLoad(_overlay);

                var go = new GameObject("Veil", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_overlay.transform, false);
                _veil = go.GetComponent<Image>();
                _veil.color = new Color(0f, 0f, 0f, 0f);
                // The veil must eat taps mid-fade — an input landing on the outgoing scene during
                // a load would double-issue whatever command it hit.
                _veil.raycastTarget = true;
                RectTransform rect = _veil.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            _overlay.SetActive(true);
            Color color = _veil.color;
            color.a = 0f;
            _veil.color = color;
        }

        private static GameObject EnsureHost()
        {
            if (_host == null)
            {
                _host = new GameObject("SceneTransitionHost", typeof(TransitionHost));
                UnityEngine.Object.DontDestroyOnLoad(_host);
            }

            return _host;
        }

        private static void Teardown()
        {
            IsTransitioning = false;
            _fadingIn = false;
            if (_overlay != null)
            {
                UnityEngine.Object.Destroy(_overlay);
                _overlay = null;
                _veil = null;
            }
        }

        /// <summary>
        /// Hidden coroutine host. Coroutines need a live MonoBehaviour; this one owns no state and is
        /// destroyed with the play session.
        /// </summary>
        private sealed class TransitionHost : MonoBehaviour
        {
            public void Run(IEnumerator routine) => StartCoroutine(routine);
        }
    }
}

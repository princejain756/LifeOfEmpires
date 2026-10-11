using System.Collections.Generic;
using System.IO;
using System.Reflection;
using LifeOfEmpires.Audio;
using NUnit.Framework;
using UnityEngine;

namespace LifeOfEmpires.Tests.EditMode
{
    /// <summary>
    /// Guards the one failure mode that stays invisible to every other gate: a scene whose
    /// <c>AudioManager.music</c> names something <c>Resources.Load</c> cannot resolve plays nothing
    /// while compiling, bootstrapping and testing perfectly green.
    ///
    /// The scene half reads the authored YAML rather than instantiating it, because the bug lives in
    /// the serialized value — the same value <c>ProjectBootstrap</c> re-authors on every run.
    /// </summary>
    public class AudioPlaybackWiringTests
    {
        private static readonly string[] Stems =
        {
            "Music_Menu", "Music_Battle", "Sting_Victory", "Sting_Defeat"
        };

        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            _host = null;
        }

        /// <summary>
        /// A live manager of the shipping type, not a stub: the wiring under test is Awake's.
        ///
        /// EditMode never delivers MonoBehaviour lifecycle callbacks, so Awake is invoked the same way
        /// <c>ArcherTests</c> drives Update's callback — by reflection. That is the only reason the
        /// shipping initialisation is reachable from an edit-mode test at all.
        /// </summary>
        private AudioManager BuildManager()
        {
            _host = new GameObject("AudioManagerTestHost");
            AudioManager manager = _host.AddComponent<AudioManager>();
            typeof(AudioManager)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, null);
            return manager;
        }

        private static string ScenePath(string scene) =>
            Path.Combine(Application.dataPath, "LifeOfEmpires", "Scenes", scene + ".unity");

        [Test]
        public void EveryAuthoredStemResolvesToANonEmptyClip()
        {
            foreach (string stem in Stems)
            {
                AudioClip clip = Resources.Load<AudioClip>(stem);
                Assert.IsNotNull(clip, $"Resources.Load<AudioClip>(\"{stem}\") resolved nothing.");
                Assert.Greater(clip.samples, 0, $"{stem} imported with zero samples.");
                Assert.GreaterOrEqual(clip.channels, 1, $"{stem} imported with no channels.");
                Assert.Greater(clip.length, 1f, $"{stem} is shorter than one second — likely a failed import.");
            }
        }

        [Test]
        public void PlayMusicAssignsTheRequestedClip()
        {
            AudioManager manager = BuildManager();
            Assert.IsNotNull(manager.musicSource, "Awake did not create the music source.");

            manager.PlayMusic("Music_Menu");
            Assert.AreSame(Resources.Load<AudioClip>("Music_Menu"), manager.musicSource.clip);

            // Switching tracks must replace the clip, not stack a second one.
            manager.PlayMusic("Music_Battle");
            Assert.AreSame(Resources.Load<AudioClip>("Music_Battle"), manager.musicSource.clip);
        }

        [Test]
        public void PlayMusicWithAnEmptyNameStopsAndClears()
        {
            AudioManager manager = BuildManager();

            manager.PlayMusic("Music_Menu");
            Assert.IsNotNull(manager.musicSource.clip);

            manager.PlayMusic(string.Empty);
            Assert.IsNull(manager.musicSource.clip, "An empty track name must leave the source silent.");
        }

        [Test]
        public void PlayMusicWithAnUnknownNameLeavesTheClipIntact()
        {
            AudioManager manager = BuildManager();

            manager.PlayMusic("Music_Menu");
            AudioClip before = manager.musicSource.clip;

            Assert.DoesNotThrow(() => manager.PlayMusic("nope"));
            Assert.AreSame(before, manager.musicSource.clip, "A bad name must not silence a good track.");
        }

        [Test]
        public void EverySceneWiresAnAudibleMusicStem()
        {
            var expected = new Dictionary<string, string>
            {
                { "Battle", "Music_Battle" },
                { "Bootstrap", "Music_Menu" },
                { "CampaignMap", "Music_Menu" },
                { "Skirmish", "Music_Menu" },
                { "Kingdom", "Music_Menu" }
            };

            foreach (KeyValuePair<string, string> scene in expected)
            {
                string value = SingleMusicValue(scene.Key);
                Assert.AreEqual(scene.Value, value, $"{scene.Key}.unity names the wrong track.");
                Assert.IsNotNull(Resources.Load<AudioClip>(value),
                    $"{scene.Key}.unity names '{value}', which Resources.Load cannot resolve — the scene is silent.");
            }
        }

        /// <summary>
        /// The <c>music:</c> value of the scene's single AudioManager component. Keyed on
        /// <c>m_EditorClassIdentifier</c> so a re-generated script GUID cannot hide the wiring.
        /// </summary>
        private static string SingleMusicValue(string scene)
        {
            string[] lines = File.ReadAllLines(ScenePath(scene));
            var values = new List<string>();
            bool insideManager = false;

            foreach (string line in lines)
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("m_EditorClassIdentifier:"))
                {
                    insideManager = trimmed.EndsWith("LifeOfEmpires.Audio.AudioManager");
                }
                else if (insideManager && trimmed.StartsWith("music:"))
                {
                    values.Add(trimmed.Substring("music:".Length).Trim());
                    insideManager = false;
                }
            }

            Assert.AreEqual(1, values.Count, $"{scene}.unity must hold exactly one AudioManager.");
            Assert.IsNotEmpty(values[0], $"{scene}.unity has an empty music name; the scene ships silent.");
            return values[0];
        }
    }
}

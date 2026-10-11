using UnityEngine;

namespace LifeOfEmpires.Audio
{
    /// <summary>
    /// One per scene. Owns the scene's single <see cref="AudioListener"/> and the two sources every
    /// clip plays through, so nothing else in the project has to author audio objects and no scene can
    /// accidentally end up with two listeners — two listeners is the silent-audio bug that is hardest
    /// to spot in the editor.
    ///
    /// Consumers resolve through <see cref="Current"/> at the point of use rather than caching a
    /// reference. Teardown paths (<c>GameBootstrap.OnDestroy</c>, <c>TestWorld.TearDown</c>) call
    /// <see cref="ServiceLocator.Clear"/>, so a cached reference can dangle at a destroyed object.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        /// <summary>
        /// <c>Resources</c> clip name to play on scene start: "Music_Menu", "Music_Battle", or empty for
        /// silence. It is a file stem, not a semantic key — <see cref="PlayMusic"/> resolves it through
        /// <c>Resources.Load</c>, so any other spelling (e.g. "menu") loads nothing and the scene runs mute.
        /// </summary>
        public string music = "";

        public AudioSource musicSource;
        public AudioSource effectSource;

        /// <summary>
        /// Fallback preferences used only when no save could be read at all. The values are the
        /// authored <see cref="SaveData"/> field initialisers, not literals re-stated here, so the
        /// "what is audible by default" answer lives in exactly one place.
        /// </summary>
        private static readonly SaveData Defaults = new SaveData();

        private SaveData _save;

        /// <summary>The live manager, or null when no scene with one is loaded.</summary>
        public static AudioManager Current { get; private set; }

        /// <summary>Music gain, forced to zero when the preference says muted.</summary>
        public float MusicVolume => _save != null && _save.musicMuted
            ? 0f
            : Mathf.Clamp01(_save != null ? _save.musicVolume : Defaults.musicVolume);

        /// <summary>Effects gain, forced to zero when the preference says muted.</summary>
        public float EffectVolume => _save != null && _save.sfxMuted
            ? 0f
            : Mathf.Clamp01(_save != null ? _save.sfxVolume : Defaults.sfxVolume);

        private void Awake()
        {
            if (Current == null) Current = this;

            // A scene with no listener plays nothing at all, however many clips it references.
            if (GetComponent<AudioListener>() == null) gameObject.AddComponent<AudioListener>();

            musicSource = AddSource(loop: true);
            effectSource = AddSource(loop: false);

            _save = SaveSystem.ReadFromDisk();
            ApplyVolumes();
        }

        private void Start()
        {
            // Registered in Start, not Awake: GameBootstrap registers its own services in Start and a
            // teardown that runs between the two would otherwise drop this key immediately.
            ServiceLocator.Register(this);
            PlayMusic(music);
        }

        /// <summary>An AudioSource on this object. Childless — it needs no transform of its own.</summary>
        private AudioSource AddSource(bool loop)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            return source;
        }

        /// <summary>Play a looping track by <c>Resources</c> name. Empty stops; unknown warns.</summary>
        public void PlayMusic(string track)
        {
            if (string.IsNullOrEmpty(track))
            {
                StopMusic();
                return;
            }

            AudioClip clip = Resources.Load<AudioClip>(track);
            if (clip == null)
            {
                Debug.LogWarning($"AudioManager: no clip named '{track}'; staying silent.");
                return;
            }

            if (musicSource.clip == clip && musicSource.isPlaying) return;

            musicSource.clip = clip;
            ApplyVolumes();
            musicSource.Play();
        }

        public void StopMusic()
        {
            if (musicSource == null) return;

            musicSource.Stop();
            musicSource.clip = null;
        }

        /// <summary>
        /// One-shot effect. Silent when the clip is missing: an SFX lookup that fails is not worth a
        /// console warning on the frame a player wins a battle.
        /// </summary>
        public void PlayEffect(string clipName)
        {
            if (EffectVolume <= 0f) return;

            AudioClip clip = Resources.Load<AudioClip>(clipName);
            if (clip == null) return;

            effectSource.PlayOneShot(clip);
        }

        /// <summary>
        /// The single outcome fence. Victory and defeat cut the battle loop and play a sting; an
        /// in-progress outcome does nothing, so a stray call cannot interrupt the score.
        /// </summary>
        public void PlayOutcomeSting(BattleOutcome outcome)
        {
            if (outcome == BattleOutcome.Victory)
            {
                StopMusic();
                PlayEffect("Sting_Victory");
            }
            else if (outcome == BattleOutcome.Defeat)
            {
                StopMusic();
                PlayEffect("Sting_Defeat");
            }
        }

        public void ApplyVolumes()
        {
            if (musicSource != null) musicSource.volume = MusicVolume;
            if (effectSource != null) effectSource.volume = EffectVolume;
        }

        /// <summary>
        /// Persist a music preference and apply it immediately. Reads the save fresh so a change made
        /// from a screen other than the one that wrote the file can never be clobbered.
        /// </summary>
        public void SetMusicVolume(float volume, bool muted)
        {
            _save = SaveSystem.ReadFromDisk();
            _save.musicVolume = Mathf.Clamp01(volume);
            _save.musicMuted = muted;
            SaveSystem.WriteToDisk(_save);
            ApplyVolumes();
        }

        /// <summary>Persist an effects preference and apply it immediately.</summary>
        public void SetEffectVolume(float volume, bool muted)
        {
            _save = SaveSystem.ReadFromDisk();
            _save.sfxVolume = Mathf.Clamp01(volume);
            _save.sfxMuted = muted;
            SaveSystem.WriteToDisk(_save);
            ApplyVolumes();
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;

            // Never Clear(): GameBootstrap and the test harnesses own that call, and firing it here
            // would wipe unrelated registrations whenever a scene is torn down.
            ServiceLocator.Unregister<AudioManager>();
        }
    }
}

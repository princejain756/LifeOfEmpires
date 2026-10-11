#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LifeOfEmpires.Editor
{
    /// <summary>
    /// Writes a placeholder WAV for any clip the authored audio folder is missing.
    ///
    /// The preferred path is the spec's local generator (<c>MAKERS MD FILES/audiogeneration.md</c>);
    /// this exists so a build can never ship silent because generation was skipped, failed, or a new
    /// clip name was added. It never overwrites an existing file, so running it after a real generation
    /// pass is a no-op.
    /// </summary>
    public static class GenerateAudioAssets
    {
        private const string AudioFolder = "Assets/LifeOfEmpires/Audio";
        private const string ResourcesFolder = "Assets/LifeOfEmpires/Resources";
        private const int SampleRate = 44100;

        [MenuItem("LifeOfEmpires/Generate Missing Audio Clips")]
        public static void Run()
        {
            // Both folders must already exist: they carry authored GUIDs, and CreateFolder here would
            // mint new ones and break every reference to them.
            if (!AssetDatabase.IsValidFolder(AudioFolder)) throw new InvalidOperationException($"Missing '{AudioFolder}'.");
            if (!AssetDatabase.IsValidFolder(ResourcesFolder)) throw new InvalidOperationException($"Missing '{ResourcesFolder}'.");

            WriteIfMissing("Music_Menu", 12f, loopable: true);
            WriteIfMissing("Music_Battle", 12f, loopable: true);
            WriteIfMissing("Sting_Victory", 3f, loopable: false);
            WriteIfMissing("Sting_Defeat", 3f, loopable: false);

            AssetDatabase.Refresh();
            Debug.Log("AUDIO_GENERATED_OK");
        }

        private static void WriteIfMissing(string name, float seconds, bool loopable)
        {
            string primary = $"{AudioFolder}/{name}.wav";
            if (File.Exists(primary)) return;

            float[] data = Synthesise(name, seconds, loopable);
            WriteWav(primary, data);
            WriteWav($"{ResourcesFolder}/{name}.wav", data);
            Debug.Log($"GenerateAudioAssets: wrote placeholder '{name}.wav' ({seconds}s).");
        }

        /// <summary>Root note per clip, chosen so each sting sits in its own register.</summary>
        private static float RootFrequency(string name)
        {
            if (name == "Music_Battle") return 146.83f;
            if (name == "Sting_Defeat") return 110f;
            if (name == "Sting_Victory") return 196f;
            return 174.61f;
        }

        private static float[] Synthesise(string name, float seconds, bool loopable)
        {
            float root = RootFrequency(name);
            float third = name == "Music_Battle" ? 1.1892f : 1.2599f;
            int count = Mathf.RoundToInt(SampleRate * seconds);
            var data = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = loopable
                    // One full 4-second breath per 4 seconds, so a 12-second clip ends where it began.
                    ? 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 0.5f)
                    : Mathf.Min(Mathf.Clamp01(t / 0.05f), Mathf.Clamp01((seconds - t) / 0.35f));

                data[i] = envelope * 0.32f * (Mathf.Sin(2f * Mathf.PI * root * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * root * third * t));
            }

            return data;
        }

        /// <summary>Canonical 44-byte PCM header, then little-endian 16-bit samples.</summary>
        private static void WriteWav(string path, float[] data)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(stream);

            int dataBytes = data.Length * 2;
            // Little-endian uint writes spell the four ASCII chunk tags byte for byte, so the header
            // never depends on an encoder's behaviour.
            writer.Write(0x46464952u);     // "RIFF"
            writer.Write(36 + dataBytes);
            writer.Write(0x45564157u);     // "WAVE"

            writer.Write(0x20746D66u);     // "fmt "
            writer.Write(16);              // PCM fmt chunk size
            writer.Write((short)1);        // PCM
            writer.Write((short)1);        // mono
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);  // byte rate
            writer.Write((short)2);        // block align
            writer.Write((short)16);       // bits per sample

            writer.Write(0x61746164u);     // "data"
            writer.Write(dataBytes);
            for (int i = 0; i < data.Length; i++)
            {
                writer.Write((short)(Mathf.Clamp(data[i], -1f, 1f) * short.MaxValue));
            }
        }
    }
}
#endif

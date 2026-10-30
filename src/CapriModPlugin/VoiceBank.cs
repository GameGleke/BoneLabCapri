using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace CapriMod
{
    /// <summary>
    /// Хранилище озвучки Капри. Все WAV-клипы встроены (EmbeddedResource) прямо в DLL мода —
    /// игроку достаточно одного файла CapriMod.dll в папке Mods.
    /// Файлы названы "категория_NN.wav" (spawn_01.wav, attack_03.wav и т.д.), список — в manifest.txt.
    /// </summary>
    public static class VoiceBank
    {
        public enum Category { Spawn, Telekinesis, Attack, NoTarget, ChipRemoved, Idle, Death }

        private static readonly Dictionary<Category, List<AudioClip>> Clips = new();
        private static bool _loaded;
        private static int _lastIndex = -1;
        private static float _lastSpeakTime = -999f;

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            var asm = Assembly.GetExecutingAssembly();
            foreach (var resName in asm.GetManifestResourceNames())
            {
                if (!resName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) continue;

                string file = Path.GetFileName(resName);
                string catRaw = file.Split('_')[0].ToLowerInvariant();
                if (!TryMapCategory(catRaw, out var cat)) continue;

                try
                {
                    byte[] data;
                    using (var s = asm.GetManifestResourceStream(resName))
                    {
                        data = new byte[s.Length];
                        s.Read(data, 0, data.Length);
                    }
                    // Unity может деколировать WAV напрямую из памяти
                    var clip = AudioClip.Create("capri_" + file, 1, 1, 22050, false);
                    clip = DecompressWav(data, file);
                    if (clip == null) continue;
                    if (!Clips.TryGetValue(cat, out var list))
                        list = Clips[cat] = new List<AudioClip>();
                    list.Add(clip);
                }
                catch (Exception e)
                {
                    MelonLogger.Error($"VoiceBank: не удалось загрузить {file}: {e.Message}");
                }
            }

            int total = 0;
            foreach (var kv in Clips) total += kv.Value.Count;
            MelonLogger.Msg($"VoiceBank: загружено клипов Капри: {total}");
        }

        private static bool TryMapCategory(string raw, out Category cat)
        {
            switch (raw)
            {
                case "spawn": cat = Category.Spawn; return true;
                case "telekinesis": cat = Category.Telekinesis; return true;
                case "attack": cat = Category.Attack; return true;
                case "notarget": cat = Category.NoTarget; return true;
                case "chip_removed": cat = Category.ChipRemoved; return true;
                case "idle": cat = Category.Idle; return true;
                case "death": cat = Category.Death; return true;
                default: cat = default; return false;
            }
        }

        /// <summary>Загрузка WAV через WWW/UnityWebRequestTexture недопустима в IL2CPP без coroutine,
        /// поэтому используем UnityWebRequestMultimedia через корутину Main-монобехавиора.
        /// Здесь — синтетический путь: читаем PCM-данные вручную.</summary>
        private static AudioClip DecompressWav(byte[] wav, string name)
        {
            // Минимальный парсер RIFF/WAVE (PCM 16-bit mono) — формат, который генерирует наш TTS-скрипт.
            if (wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F')
                return null;
            int pos = 12;
            int channels = 1, sampleRate = 22050;
            short[] samples = null;
            while (pos + 8 <= wav.Length)
            {
                string id = Encoding.ASCII.GetString(wav, pos, 4);
                int size = BitConverter.ToInt32(wav, pos + 4);
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(wav, pos + 10);
                    sampleRate = BitConverter.ToInt32(wav, pos + 12);
                }
                else if (id == "data")
                {
                    int count = Math.Min(size / 2, (wav.Length - pos - 8) / 2);
                    samples = new short[count];
                    Buffer.BlockCopy(wav, pos + 8, samples, 0, count * 2);
                    break;
                }
                pos += 8 + size + (size % 2);
            }
            if (samples == null || samples.Length == 0) return null;

            float[] data = new float[samples.Length];
            for (int i = 0; i < samples.Length; i++) data[i] = samples[i] / 32768f;

            var clip = AudioClip.Create(name, data.Length, channels, sampleRate, false);
            if (!clip.SetData(data, 0)) return null;
            return clip;
        }

        /// <summary>Случайный клип категории (без повтора предыдущего).</summary>
        public static AudioClip GetRandom(Category cat)
        {
            if (!Clips.TryGetValue(cat, out var list) || list.Count == 0) return null;
            int idx;
            do { idx = UnityEngine.Random.Range(0, list.Count); }
            while (list.Count > 1 && idx == _lastIndex && cat == Category.Spawn);
            _lastIndex = idx;
            return list[idx];
        }

        /// <summary>Не давать фразам накладываться друг на друга.</summary>
        public static bool CanSpeak(float minInterval = 2.5f) =>
            Time.time - _lastSpeakTime > minInterval;

        public static void MarkSpoke() => _lastSpeakTime = Time.time;
    }
}

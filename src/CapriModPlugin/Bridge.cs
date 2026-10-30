using System;
using System.Reflection;
using Il2CppSLZ.Bonelab;
using Il2CppSLZ.Marrow;
using MelonLoader;
using UnityEngine;

namespace CapriMod
{
    /// <summary>
    /// Мост между Unity-объектами мода и IL2CPP-миром игры + «фабрика чипа».
    ///
    /// BONELAB работает на IL2CPP: типы SLZ.* (RigManager, Hand, Barcode...) — Il2Cpp-классы,
    /// а наши MonoBehaviour'ы — чистый Mono. Смешивать их в одном потоке нельзя без switch'а,
    /// поэтому вся «чистая» логика живёт здесь и дергается только из Main (Mono-safe поток),
    /// как в BoneLib (BoneLib.cs -> Instances.Runner).
    /// </summary>
    public static class Bridge
    {
        private static CapriController _controller;

        // очередь событий из Harmony-хуков (IL2CPP-поток) -> Main.OnUpdate (Mono-поток)
        private static volatile bool _deathFlag;
        private static volatile bool _handAttachFlag;
        private static float _ensureChipRequestedAt = -1f;

        /// <summary>Вызывается из Main.OnInitializeMelon (Mono-поток).</summary>
        public static void Init()
        {
            var go = new GameObject("CapriModRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            Main.Runner = go;
            _controller = go.AddComponent<CapriController>();
            MelonLogger.Msg("Bridge: runner создан.");
        }

        /// <summary>Из хука Hand.AttachObject: предмет «прилип» к руке — ставим флаг для Mono-потока.</summary>
        public static void NotifyHandAttachFromHook() => _handAttachFlag = true;

        /// <summary>Из хука Player_Health.Death: ставим флаг (не трогаем Mono-объекты!).</summary>
        public static void NotifyDeathFromHook() => _deathFlag = true;

        /// <summary>Запросить чип после загрузки уровня / спавна игрока. Вызывается только с main thread игры.</summary>
        public static void EnsureChipRequested()
        {
            try { _ensureChipRequestedAt = Time.time; }
            catch { _ensureChipRequestedAt = Environment.TickCount / 1000f; }
        }

        /// <summary>Вызывается из Main.OnUpdate (Mono-поток).</summary>
        public static void ProcessFlags()
        {
            if (_handAttachFlag)
            {
                _handAttachFlag = false;
                CapriChip.CheckProximityToHead(); // Mono-поток: проверяем, не «вставляем» ли чип
            }
            if (_deathFlag)
            {
                _deathFlag = false;
                _controller?.NotifyPlayerDeath();
            }
            // Спавним чип чуть позже, когда rig уже готов
            if (_ensureChipRequestedAt > 0 && Main.PlayerReady &&
                Time.time - _ensureChipRequestedAt > 1.5f)
            {
                _ensureChipRequestedAt = -1f;
                EnsureChipInScene();
            }
        }

        // ================= ФАБРИКА ЧИПА =================
        // Мод сам создаёт предмет «CHIP OF CAPRI» при загрузке уровня —
        // все ассеты внутри DLL, отдельных папок не нужно.

        public const string CHIP_NAME = "CHIP OF CAPRI";

        public static void EnsureChipInScene()
        {
            try
            {
                if (CapriChip.Active != null) return; // уже есть
                SpawnChipNearPlayer();
            }
            catch (Exception e)
            {
                MelonLogger.Error($"Bridge.EnsureChipInScene: {e}");
            }
        }

        private static void SpawnChipNearPlayer()
        {
            var rig = Main.Rig;
            if (rig == null) return;
            Vector3 pos = GameBridge.HeadPosition + new Vector3(0.8f, 0.2f, 0.4f);

            // --- тело чипа: маленький «кибернетический имплант» ---
            var chipGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chipGO.name = CHIP_NAME;
            chipGO.transform.position = pos;
            chipGO.transform.localScale = new Vector3(0.09f, 0.03f, 0.07f);

            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.1f, 0.9f, 0.85f);          // бирюзовый cyberpunk
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.05f, 0.5f, 0.45f));
            chipGO.GetComponent<Renderer>().material = mat;

            // --- физика, чтобы игрушку можно было взять руками ---
            var rb = chipGO.AddComponent<Rigidbody>();
            rb.mass = 0.15f;

            // Поверхность захвата Marrow: добавляем НЕ напрямую (Marrows требует
            // прокинутых serialized-полей — NRE на Awake), а копированием с готового
            // предмета в сцене. Если скопировать нечего — чип остаётся обычным
            // физическим объектом с коллайдером: руки BoneLab берут и такие.
            TryCopyGrabSurface(chipGO);

            // --- наш скрипт-чип: детект «вставки в голову» по дистанции ---
            chipGO.AddComponent<CapriChip>();

            MelonLogger.Msg($"Bridge: создан предмет {CHIP_NAME} возле игрока.");
        }

        /// <summary>Пытаемся скопировать компонент(ы) захвата с существующего интерактивного предмета.</summary>
        private static void TryCopyGrabSurface(GameObject go)
        {
            try
            {
                foreach (var rb in UnityEngine.Object.FindObjectsOfType<Rigidbody>())
                {
                    if (rb.gameObject == go) continue;
                    // берём первый предмет, у которого есть компонент из namespace SLZ.Marrow (Marrows и т.п.)
                    bool copied = false;
                    foreach (var comp in rb.GetComponents<Component>())
                    {
                        if (comp == null) continue;
                        var t = comp.GetType();
                        if (t.Namespace == null || !t.Namespace.StartsWith("SLZ.Marrow")) continue;
                        if (t.Name == "Rigidbody" || t.Name.Contains("Health") || t.Name.Contains("Brain")) continue;
                        CopyComponent(comp, go);
                        copied = true;
                    }
                    if (copied)
                    {
                        MelonLogger.Msg("Bridge: поверхность захвата скопирована с игрового предмета.");
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"Bridge: TryCopyGrabSurface — {e.Message} (не критично).");
            }
        }

        private static void CopyComponent(Component original, GameObject destination)
        {
            var type = original.GetType();
            var dest = destination.GetComponent(type);
            if (dest == null) dest = destination.AddComponent(type);
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.IsStatic || field.FieldType == typeof(Transform) || field.FieldType == typeof(Rigidbody)) continue;
                try { field.SetValue(dest, field.GetValue(original)); } catch { }
            }
            foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                if (prop.Name == "transform" || prop.Name == "gameObject" || prop.Name == "name") continue;
                try { prop.SetValue(dest, prop.GetValue(original, null), null); } catch { }
            }
        }
    }
}

using System;
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
        private static RigManager _pendingLocalRig;
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

        /// <summary>Из хука PlayerMarker: передаём локальный rig в Mono-поток.</summary>
        public static void NotifyPlayerSpawnFromHook(RigManager rig) => _pendingLocalRig = rig;

        /// <summary>Из хука Hand.AttachObject: предмет «прилип» к руке — ставим флаг для Mono-потока.</summary>
        public static void NotifyHandAttachFromHook() => _handAttachFlag = true;

        /// <summary>Из хука Player_Health.Death: ставим флаг (не трогаем Mono-объекты!).</summary>
        public static void NotifyDeathFromHook() => _deathFlag = true;

        /// <summary>Запросить чип после загрузки уровня (из OnSceneWasLoaded — тоже Mono-поток Melon).</summary>
        public static void EnsureChipInSceneDelayed() => _ensureChipRequestedAt = Time.time;

        /// <summary>Вызывается из Main.OnUpdate (Mono-поток).</summary>
        public static void ProcessFlags()
        {
            if (_pendingLocalRig != null)
            {
                var rig = _pendingLocalRig;
                _pendingLocalRig = null;
                GameBridge.SetLocalRig(rig);
                EnsureChipInScene();
                MelonLogger.Msg("Bridge: локальный игрок закеширован, чип проверен.");
            }
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
            // На новых сценах спавним чип чуть позже, когда rig уже готов
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

        private const string CHIP_NAME = "CHIP OF CAPRI";

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
            var marrows = chipGO.AddComponent<Marrows>();       // поверхность захвата (SLZ.Marrow)
            marrows.m_CaptureScaleMultiplier = 1.6f;

            // --- зона взаимодействия: поднёс к голове -> «вставил чип» ---
            var inter = chipGO.AddComponent<InteractTrigger>();
            inter.m_MaxGrabDistance = 0.35f;
            inter.m_LocalPosition = new Vector3(0, 0, 0);
            inter.m_LocalRotation = Quaternion.identity;
            inter.m_InteractLayerMask = LayerMask.GetMask("Headpoint");

            // --- наш скрипт-чип ---
            chipGO.AddComponent<CapriChip>();

            // --- подпись над чипом ---
            var label = new GameObject("ChipLabel");
            label.transform.SetParent(chipGO.transform, false);
            label.transform.localPosition = new Vector3(0, 0.06f, 0);
            var tmp = label.AddComponent<TMPro.TextMeshPro>();
            tmp.text = "<color=#00E6D0>" + CHIP_NAME + "</color>";
            tmp.fontSize = 0.05f;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;

            MelonLogger.Msg($"Bridge: создан предмет {CHIP_NAME} возле игрока.");
        }
    }
}

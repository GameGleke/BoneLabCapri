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
                Main.Rig = rig;
                Main.LeftHand = rig.leftHand;
                Main.RightHand = rig.rightHand;
                EnsureChipInScene();
                MelonLogger.Msg("Bridge: локальный игрок закеширован, чип проверен.");
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
        // все ассеты (модель-примитив, озвучка, логика) уже внутри DLL.

        private const string CHIP_NAME = "CHIP OF CAPRI";

        public static void EnsureChipInScene()
        {
            try
            {
                if (UnityEngine.Object.FindObjectOfType<CapriChip>() != null) return; // уже есть
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
            Vector3 pos = rig.transform.position + new Vector3(0.8f, 1.05f, 0.4f);

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

            // --- физика, чтобы игрушку можно было взять ---
            var rb = chipGO.AddComponent<Rigidbody>();
            rb.mass = 0.15f;
            var marrows = chipGO.AddComponent<Marrows>();       // поверхность захвата (SLZ.Marrow)
            marrows.m_CaptureScaleMultiplier = 1.5f;

            // --- экипируемость: слот Headwear (чип «в голову», как у Санки) ---
            var slot = chipGO.AddComponent<EquippableSlot>();   // SLZ.Marrow.EquippableSlot
            slot.m_EquippableLayerMask = LayerMask.GetMask("Default");
            try { SetSlotTypeHeadwear(slot); }
            catch (Exception e) { MelonLogger.Warning($"EquippableSlot type: {e.Message}"); }

            // --- наш скрипт-чип ---
            chipGO.AddComponent<CapriChip>();

            // --- подпись над чипом ---
            var label = new GameObject("ChipLabel");
            label.transform.SetParent(chipGO.transform, false);
            label.transform.localPosition = new Vector3(0, 0.06f, 0);
            var tmp = label.AddComponent<TMPro.TextMeshPro>();
            tmp.text = $"<color=#00E6D0>{CHIP_NAME}</color>";
            tmp.fontSize = 0.05f;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;

            MelonLogger.Msg($"Bridge: создан предмет {CHIP_NAME} возле игрока.");
        }

        private static void SetSlotTypeHeadwear(EquippableSlot slot)
        {
            // enum-поле m_SlotType: ищем значение Headwear рефлексией (enum может быть приватным в interop)
            var field = typeof(EquippableSlot).GetField("m_SlotType",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            if (field == null) return;
            var enumType = field.FieldType;               // Il2CppEnum => struct-подобный
            foreach (var name in Enum.GetNames(enumType))
            {
                if (name.Equals("Headwear", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Hat", StringComparison.OrdinalIgnoreCase))
                {
                    object val = Enum.Parse(enumType, name);
                    field.SetValue(slot, val);
                    break;
                }
            }
        }
    }
}

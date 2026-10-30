using System;
using HarmonyLib;
using Il2CppSLZ.Bonelab;
using Il2CppSLZ.Marrow;
using MelonLoader;
using UnityEngine;

namespace CapriMod
{
    /// <summary>
    /// BoneLabCapri — Капри как конструкт из чипа (Cyberpunk: Edgerunners) в BONELAB VR.
    ///
    /// Механики:
    ///  • CHIP OF CAPRI — предмет, экипируемый в слот головы («чип вставляется в голову», как у Санки).
    ///    Пока надет:
    ///      - Капри периодически появляется в случайных местах уровня и видна ТОЛЬКО этому игроку;
    ///      - говорит заранее озвученные фразы (голос — та же модель, что в Capricorn_ai_qwen);
    ///      - телекинез: удерживай Grip — ближайший физический объект притягивается к руке;
    ///      - «Scorch»: удерживай Trigger — периодический урон цели, на которую смотришь,
    ///        растущий с длительностью удержания.
    ///  • Снял чип — Капри исчезает с репликой прощания.
    /// </summary>
    public class Main : MelonMod
    {
        internal static Main Instance { get; private set; }
        internal static GameObject Runner { get; private set; }

        // Кэш игрока (заполняется через Bridge из хука PlayerMarker)
        internal static RigManager Rig;
        internal static Hand LeftHand, RightHand;
        internal static bool PlayerReady => Rig != null && GameBridge.FindHeadpoint() != null;

        public override void OnInitializeMelon()
        {
            Instance = this;
            LoggerInstance.Msg($"{BuildInfo.Name} v{BuildInfo.Version}: {BuildInfo.Description}");

            VoiceBank.Load();
            Config.LoadOrCreate();
            Bridge.Init();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (buildIndex == 0 || sceneName.StartsWith("Menu"))
            {
                Rig = null; LeftHand = null; RightHand = null;
            }
            else
            {
                // Чип должен появиться на новых песочницах/уровнях
                Bridge.EnsureChipRequested();
            }
        }

        public override void OnUpdate()
        {
            PlayerLocator.Tick();
            Bridge.ProcessFlags();
#if DEBUG
            if (Input.GetKeyDown(KeyCode.F12))
                LoggerInstance.Msg($"ChipOnHead={CapriController.ChipEquipped}, entity={(CapriEntity.Instance != null)}");
#endif
        }
    }

    /// <summary>
    /// Детект локального RigManager — БЕЗ Harmony-хуков и приватных полей (m_Local/m_RigManager
    /// отсутствуют в некоторых версиях interop-сборок → CS1061). Опрашиваем rig из Mono-потока
    /// (Melon OnUpdate работает на main thread игры, как в BoneLib Hooking.cs).
    /// </summary>
    internal static class PlayerLocator
    {
        private static float _nextScan = -1f;
        private static bool _spawnNotified;

        public static void Tick()
        {
            // rig ещё не найден — сканируем раз в секунду
            if (Main.Rig == null || !Main.Rig.gameObject.activeInHierarchy)
            {
                if (Time.time < _nextScan) return;
                _nextScan = Time.time + 1f;
                RigManager found = UnityEngine.Object.FindObjectOfType<RigManager>();
                if (found != null && found.gameObject.activeInHierarchy)
                {
                    GameBridge.SetLocalRig(found);
                    Bridge.EnsureChipRequested();
                    MelonLogger.Msg("PlayerLocator: локальный RigManager закеширован.");
                }
                return;
            }
            // rig есть — как только голова найдена, запрашиваем чип (многократный вызов безопасен)
            if (!_spawnNotified && GameBridge.FindHeadpoint() != null)
            {
                _spawnNotified = true;
                Bridge.EnsureChipRequested();
            }
        }
    }

    /// <summary>Хук захвата предмета рукой — после AttachObject проверяем, не «вставляем» ли чип в голову.
    /// Только публичные API (BoneLib-паттерн), никаких приватных полей.</summary>
    [HarmonyPatch(typeof(Hand), "AttachObject")]
    public static class HandAttachPatch
    {
        public static void Postfix(Hand __instance)
        {
            try
            {
                if (Main.Rig == null) return;
                // «локальность» руки проверяем в Mono-потоке (ProcessFlags), здесь только флаг
                Bridge.NotifyHandAttachFromHook();
            }
            catch (Exception e)
            {
                MelonLogger.Error($"HandAttachPatch: {e}");
            }
        }
    }

    /// <summary>Хук смерти игрока — Капри произносит прощальную реплику (паттерн BoneLib: Player_Health.Death).</summary>
    [HarmonyPatch(typeof(Player_Health), nameof(Player_Health.Death))]
    public static class PlayerDeathPatch
    {
        public static void Postfix(Player_Health __instance)
        {
            try
            {
                var rig = __instance.GetComponentInParent<RigManager>();
                if (rig != null && rig == Main.Rig) Bridge.NotifyDeathFromHook();
            }
            catch (Exception e)
            {
                MelonLogger.Error($"PlayerDeathPatch: {e}");
            }
        }
    }
}

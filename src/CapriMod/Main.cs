using System;
using HarmonyLib;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.Warehouse;
using MelonLoader;
using UnityEngine;

namespace CapriMod
{
    /// <summary>
    /// Точка входа мода BoneLabCapri.
    /// Структура и хуки — по образцу BoneLib (github.com/yowchap/BoneLib).
    /// </summary>
    public class Main : MelonMod
    {
        internal static Main Instance { get; private set; }

        // Ссылки на игрока (кэшируются при спавне, как в BoneLib/Player.cs)
        internal static RigManager RigManager { get; private set; }
        internal static Hand LeftHand { get; private set; }
        internal static Hand RightHand { get; private set; }
        internal static bool PlayerReady => RigManager != null && LeftHand != null && RightHand != null;

        public override void OnInitializeMelon()
        {
            Instance = this;
            LoggerInstance.Msg($"{BuildInfo.Name} v{BuildInfo.Version} загружен.");

            // Harmony-хуки игровых методов
            Hooking.InitHooks(HarmonyInstance);
        }

        public override void OnUpdate()
        {
            // Отладка: F12 печатает предмет в правой руке
#if DEBUG
            if (Input.GetKeyDown(KeyCode.F12) && PlayerReady)
            {
                var held = RightHand.m_CurrentAttachedGO;
                LoggerInstance.Msg(held != null ? $"В руке: {held.name}" : "Рука пуста");
            }
#endif
        }

        /// <summary>Кэширование ссылок на игрока при спавне (вызывается из хука PlayerMarker).</summary>
        internal static void CachePlayerReferences(RigManager rm)
        {
            RigManager = rm;
            LeftHand = rm.leftHand;
            RightHand = rm.rightHand;
        }
    }

    /// <summary>Хуки игровых событий (по образцу BoneLib/Hooking.cs).</summary>
    public static class Hooking
    {
        public static event Action OnLevelLoaded;
        public static event Action<GameObject> OnPlayerSpawnedEvent;
        public static event Action<Gun> OnPostFireGun;
        public static event Action<GameObject, Hand> OnGrabObject;

        public static void InitHooks(Harmony harmony)
        {
            // Игрок заспавнился — кэшируем руки/риг
            CreateHook(harmony,
                typeof(PlayerMarker).GetMethod(nameof(PlayerMarker.OnPlayerSpawned)),
                typeof(Hooking).GetMethod(nameof(OnPlayerSpawnedPostfix)), postfix: true);

            // Выстрел оружия
            CreateHook(harmony,
                typeof(Gun).GetMethod(nameof(Gun.OnFire)),
                typeof(Hooking).GetMethod(nameof(OnFirePostfix)), postfix: true);

            // Захват предмета рукой
            CreateHook(harmony,
                typeof(Hand).GetMethod(nameof(Hand.AttachObject)),
                typeof(Hooking).GetMethod(nameof(OnAttachObjectPostfix)), postfix: true);
        }

        public static void CreateHook(Harmony harmony, System.Reflection.MethodInfo original,
            System.Reflection.MethodInfo hook, bool postfix = false)
        {
            harmony.Patch(original,
                prefix: postfix ? null : new HarmonyMethod(hook),
                postfix: postfix ? new HarmonyMethod(hook) : null);
            MelonLogger.Instance.Msg($"Хук {(postfix ? "POSTFIX" : "PREFIX")}: " +
                $"{original.DeclaringType?.Name}.{original.Name} -> {hook.DeclaringType.Name}.{hook.Name}");
        }

        // ---- Реализации хуков ----

        private static void OnPlayerSpawnedPostfix(GameObject playerRig)
        {
            var rm = playerRig.GetComponent<RigManager>();
            if (rm != null) Main.CachePlayerReferences(rm);
            OnPlayerSpawnedEvent?.Invoke(playerRig);
            OnLevelLoaded?.Invoke();
        }

        private static void OnFirePostfix(Gun __instance) => OnPostFireGun?.Invoke(__instance);

        private static void OnAttachObjectPostfix(Hand __instance, GameObject __0)
            => OnGrabObject?.Invoke(__0, __instance);
    }
}

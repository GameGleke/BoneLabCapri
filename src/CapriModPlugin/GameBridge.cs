using System;
using System.Reflection;
using Il2CppSLZ.Bonelab;
using Il2CppSLZ.Marrow;
using MelonLoader;
using UnityEngine;

namespace CapriMod
{
    /// <summary>
    /// Единственная точка доступа к IL2CPP-объектам игры (RigManager, Hand, BaseController,
    /// Health). Все межпоточные/межверсионные вызовы изолированы здесь.
    ///
    /// ВАЖНО: RigManager — НЕ MonoBehaviour в обычном смысле (IL2CPP), поэтому FindObjectsOfType
    /// по нему не работает — получаем rig ТОЛЬКО через хук PlayerMarker.OnPlayerSpawned.
    /// </summary>
    public static class GameBridge
    {
        // ============ РИГ ИГРОКА (только локальный) ============
        private static Il2CppObjectBase _rigPtrHolder; // держим ссылку от GC

        internal static void SetLocalRig(RigManager rig)
        {
            _rigPtrHolder = rig;
            Main.Rig = rig;
            try
            {
                // публичные свойства RigManager (проверено по BoneLib): leftHand/rightHand
                var t = rig.GetIl2CppType();
                Main.LeftHand = GetHandProp(rig, t, "leftHand");
                Main.RightHand = GetHandProp(rig, t, "rightHand");
            }
            catch (Exception e) { MelonLogger.Warning($"GameBridge hands: {e.Message}"); }
        }

        /// <summary>Вызов get-метода через стандартный рефлексивный вызов Il2CppInterop
        /// (main thread — из PlayerLocator/Melon OnUpdate).</summary>
        private static Hand GetHandProp(RigManager rig, Type t, string propName)
        {
            try
            {
                foreach (var m in t.GetMethods())
                {
                    if (!string.Equals(m.Name, "get_" + propName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (m.GetParameters().Length != 0) continue;
                    return m.Invoke(rig, null) as Hand; // Il2CppInterop поддерживает reflection для il2cpp-типов
                }
            }
            catch { }
            return null;
        }

        public static Vector3 HeadPosition
        {
            get
            {
                var hp = FindHeadpoint();
                if (hp == null) return Vector3.zero;
                try { return hp.position; } catch { return Vector3.zero; }
            }
        }

        public static Quaternion HeadRotation
        {
            get
            {
                var hp = FindHeadpoint();
                if (hp == null) return Quaternion.identity;
                try { return hp.rotation; } catch { return Quaternion.identity; }
            }
        }

        /// <summary>Трансформ головы: ищем headpoint рефлексией по публичным полям/свойствам —
        /// устойчиво к переименованиям между версиями игры. Fallback — Camera.main.</summary>
        public static Transform FindHeadpoint()
        {
            var rig = Main.Rig;
            if (rig == null) return CameraFallback();
            try
            {
                var t = rig.GetIl2CppType();
                const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var name in new[] { "m_Headpoint", "headpoint", "Headpoint", "HeadPoint" })
                {
                    var fld = t.GetField(name, F);
                    if (fld != null && fld.FieldType == typeof(Transform))
                    {
                        var v = fld.GetValue(rig) as Transform;
                        if (v != null) return v;
                    }
                    var prp = t.GetProperty(name, F);
                    if (prp != null && prp.CanRead && typeof(Transform).IsAssignableFrom(prp.PropertyType))
                    {
                        var v = prp.GetValue(rig, null) as Transform;
                        if (v != null) return v;
                    }
                }
            }
            catch { /* межпоточно/версионно — падаем на камеру */ }
            return CameraFallback();
        }

        private static Transform CameraFallback()
        {
            var cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        public static Vector3 ViewDirection
        {
            get
            {
                var cam = Camera.main;
                if (cam != null && cam.transform != null) return cam.transform.forward;
                var hp = FindHeadpoint();
                if (hp != null) return hp.TransformDirection(Vector3.forward);
                return Vector3.forward;
            }
        }

        public static Vector3 ViewOrigin
        {
            get
            {
                var cam = Camera.main;
                if (cam != null && cam.transform != null) return cam.transform.position;
                return HeadPosition;
            }
        }

        // ============ КОНТРОЛЛЕРЫ (Grip / Trigger) ============
        /// <summary>Состояние кнопок руки: gripAxis/trigAxis 0..1. true — если прочитали.</summary>
        public static bool ReadControllerAxes(out float grip, out float trigger)
        {
            grip = 0f; trigger = 0f;
            var hand = Main.RightHand != null ? Main.RightHand : Main.LeftHand;
            if (hand == null) return false;
            try
            {
                // Только рефлексия по публичным полям/свойствам BaseController —
                // прямые обращения к m_GripAxis/m_TriggerAxis ломают сборку на других версиях interop.
                var c = hand.Controller;
                if (c == null) return false;
                object controllerObj = ToObject(c); // Mono-обёртка: надёжная рефлексия
                var t = controllerObj.GetType();
                object g = GetMember(t, controllerObj, "m_GripAxis") ?? GetMember(t, controllerObj, "gripAxis");
                object tr = GetMember(t, controllerObj, "m_TriggerAxis") ?? GetMember(t, controllerObj, "triggerAxis");
                if (g is float gf && tr is float tf) { grip = gf; trigger = tf; return true; }
            }
            catch { }
            return false;
        }

        /// <summary>Il2Cpp-объект -> Mono-объект через protected Il2CppObjectBase.ptr (рефлексией).</summary>
        private static object ToObject(object il2cppObj)
        {
            if (il2cppObj == null) return null;
            var ptrProp = il2cppObj.GetType().GetProperty("ptr",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (ptrProp != null)
            {
                var mono = System.Runtime.InteropServices.Marshal.PtrToStructure(
                    (IntPtr)ptrProp.GetValue(il2cppObj, null), il2cppObj.GetType());
                if (mono != null) return mono;
            }
            return il2cppObj;
        }

        private static object GetMember(Type t, object inst, string name)
        {
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var fld = t.GetField(name, F);
            if (fld != null) return fld.GetValue(inst);
            var prp = t.GetProperty(name, F);
            if (prp != null) return prp.GetValue(inst);
            return null;
        }

        // ============ ЭКИПИРОВКА ЧИПА В ГОЛОВУ ============
        /// <summary>
        /// «Вставить чип в голову»: держим предмет кинематически у headpoint (sticky-режим
        /// в CapriChip.Update). Это единственный надёжный способ без кастомного Unity-контента:
        /// JointModule/EquippableSlot требуют корректных serialized-полей ассетов игры.
        /// </summary>
        public static bool EquipChipToHead(GameObject chipGO)
        {
            if (chipGO == null || FindHeadpoint() == null) return false;
            try
            {
                // освободить от руки, иначе рука «тянет» чип и дистанция не стабилизируется
                TryDetachChipFromHand(chipGO);

                var rb = chipGO.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = true;
                chipGO.GetComponent<CapriChip>()?.SetStickyMode(true);
                return true;
            }
            catch (Exception e)
            {
                MelonLogger.Error($"GameBridge.EquipChipToHead: {e}");
                return false;
            }
        }

        /// <summary>Отцепить чип от руки — рефлексией по публичному API Hand.DetachObject,
        /// без приватных полей Grabable (их имена меняются между версиями игры).</summary>
        private static void TryDetachChipFromHand(GameObject chipGO)
        {
            foreach (var hand in new[] { Main.RightHand, Main.LeftHand })
            {
                if (hand == null) continue;
                try
                {
                    var t = hand.GetIl2CppType();
                    // 1) ищем свойство/поле с текущим захватом (AttachedGrip / attachedGrip...)
                    const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    object heldObj = null;
                    foreach (var name in new[] { "AttachedGrip", "attachedGrip", "m_AttachedGrip" })
                    {
                        var prp = t.GetProperty(name, F);
                        if (prp != null && prp.CanRead) { heldObj = prp.GetValue(hand, null); break; }
                        var fld = t.GetField(name, F);
                        if (fld != null) { heldObj = fld.GetValue(hand); break; }
                    }
                    if (heldObj == null) continue;

                    // принадлежит ли захват именно нашему чипу?
                    var goProp = heldObj.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public);
                    var heldGO = goProp?.GetValue(heldObj, null) as GameObject;
                    if (heldGO != null && heldGO != chipGO) continue;

                    // 2) вызываем DetachObject(Grabable) на руке
                    foreach (var m in t.GetMethods(F))
                    {
                        if (m.Name != "DetachObject") continue;
                        var ps = m.GetParameters();
                        if (ps.Length != 1) continue;
                        m.Invoke(hand, new[] { heldObj });
                        break;
                    }
                }
                catch { /* версия API отличается — sticky-режим всё равно прижмёт чип к голове */ }
            }
        }

        public static void UnequipChip(GameObject chipGO)
        {
            if (chipGO == null) return;
            try
            {
                chipGO.GetComponent<CapriChip>()?.SetStickyMode(false);
                var rb = chipGO.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = false;
            }
            catch (Exception e) { MelonLogger.Warning($"GameBridge.UnequipChip: {e.Message}"); }
        }

        // ============ УРОН ЦЕЛИ ============
        /// <summary>Найти цель по лучу взгляда: NPC (AIBrain/NPCHealth) или другой игрок (Player_Health).</summary>
        public static Transform FindLookTarget(float range, out GameObject targetGO)
        {
            targetGO = null;
            Ray ray = new Ray(ViewOrigin, ViewDirection);
            var hits = Physics.RaycastAll(ray, range);
            foreach (var hit in hits)
            {
                var go = hit.collider.gameObject;
                if (IsOwnPlayer(go)) continue;
                if (go.GetComponentInParent<AIBrain>() != null ||
                    go.GetComponentInParent<NPCHealth>() != null ||
                    go.GetComponentInParent<Player_Health>() != null)
                {
                    targetGO = go;
                    return hit.collider.transform;
                }
            }
            return null;
        }

        /// <summary>Компонент здоровья цели (NPCHealth у марро-NPC, Player_Health у аватаров игроков).</summary>
        public static object GetHealthComponent(GameObject targetGO, out string kind)
        {
            kind = null;
            if (targetGO == null) return null;
            var npc = targetGO.GetComponentInParent<NPCHealth>();
            if (npc != null) { kind = "npc"; return npc; }
            var ph = targetGO.GetComponentInParent<Player_Health>();
            if (ph != null) { kind = "player"; return ph; }
            return null;
        }

        /// <summary>Экранное воздействие (взрывной импульс) — визуальный фидбек scorch.</summary>
        public static void ApplyPhysicalPush(GameObject targetGO, float amount)
        {
            if (targetGO == null) return;
            try
            {
                var rb = targetGO.GetComponentInParent<Rigidbody>();
                if (rb != null)
                    rb.AddExplosionForce(Mathf.Clamp(amount * 30f, 5f, 400f), ViewOrigin, 6f, 1.5f);
            }
            catch { /* объект без физики — не страшно */ }
        }

        /// <summary>Компонент здоровья игрока (для self-damage при перегрузке).</summary>
        public static Player_Health LocalPlayerHealth()
        {
            try
            {
                var hp = FindHeadpoint();
                if (hp == null) return null;
                return hp.GetComponentInParent<Player_Health>();
            }
            catch { return null; }
        }

        private static bool IsOwnPlayer(GameObject go)
        {
            if (Main.Rig == null || go == null) return false;
            try
            {
                // RigManager — IL2CPP-объект: сравниваем напрямую (== перегружен interop'ом)
                var rm = go.GetComponentInParent<RigManager>();
                return rm != null && rm == Main.Rig;
            }
            catch { return false; }
        }

        // ============ УНИВЕРСАЛЬНЫЙ ВЫЗОВ TAKEDAMAGE ============
        /// <summary>
        /// Нанести урон компоненту здоровья рефлексией — устойчиво к разным сигнатурам
        /// TAKEDAMAGE(float damage, Vector3 direction, DamageType type, ...) между версиями игры.
        /// </summary>
        public static bool TakeDamage(object healthComp, float amount, Vector3 direction)
        {
            if (healthComp == null) return false;
            try
            {
                var type = healthComp.GetType();
                foreach (var m in type.GetMethods())
                {
                    if (!string.Equals(m.Name, "TAKEDAMAGE", StringComparison.OrdinalIgnoreCase)) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 0 || ps[0].ParameterType != typeof(float)) continue;

                    var args = new object[ps.Length];
                    args[0] = amount;
                    for (int i = 1; i < ps.Length; i++)
                    {
                        var pt = ps[i].ParameterType;
                        if (pt == typeof(float)) args[i] = 0f;
                        else if (pt == typeof(bool)) args[i] = true;
                        else if (pt == typeof(Vector3)) args[i] = direction;
                        else if (pt.IsEnum) args[i] = Activator.CreateInstance(pt);
                        else args[i] = null;
                    }
                    m.Invoke(healthComp, args);
                    return true;
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"GameBridge.TakeDamage({healthComp.GetType().Name}): {e.Message}");
            }
            return false;
        }
    }
}

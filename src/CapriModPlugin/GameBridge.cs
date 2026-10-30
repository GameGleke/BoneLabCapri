using System;
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
                Main.LeftHand = rig != null ? rig.leftHand : null;
                Main.RightHand = rig != null ? rig.rightHand : null;
            }
            catch (Exception e) { MelonLogger.Warning($"GameBridge hands: {e.Message}"); }
        }

        public static Vector3 HeadPosition =>
            Main.Rig?.m_Headpoint != null ? (Vector3)Main.Rig.m_Headpoint.position : Vector3.zero;

        public static Quaternion HeadRotation =>
            Main.Rig?.m_Headpoint != null ? (Quaternion)Main.Rig.m_Headpoint.rotation : Quaternion.identity;

        public static Vector3 ViewDirection
        {
            get
            {
                var cam = Camera.main;
                if (cam != null && cam.transform != null) return cam.transform.forward;
                if (Main.Rig?.m_Headpoint != null)
                    return Main.Rig.m_Headpoint.TransformDirection(Vector3.forward);
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
                var c = hand.Controller;
                if (c == null) return false;
                grip = c.m_GripAxis;
                trigger = c.m_TriggerAxis;
                return true;
            }
            catch
            {
                // поля могли переименоваться в другой версии игры — попробуем рефлексией
                try
                {
                    var c = hand.Controller;
                    if (c == null) return false;
                    var t = c.GetType();
                    object g = GetMember(t, c, "m_GripAxis");
                    object tr = GetMember(t, c, "m_TriggerAxis");
                    if (g is float gf && tr is float tf) { grip = gf; trigger = tf; return true; }
                }
                catch { }
                return false;
            }
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
            if (Main.Rig == null || Main.Rig.m_Headpoint == null || chipGO == null) return false;
            try
            {
                // освободить от руки, иначе рука «тянет» чип и дистанция не стабилизируется
                try
                {
                    var grab = chipGO.GetComponent<Grabable>();
                    if (grab != null)
                    {
                        var gp = grab.m_GripPoint;
                        var attached = gp?.attachedTo;
                        if (attached != null) attached.DetachObject(grab);
                    }
                }
                catch { /* не критично */ }

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
            try { return Main.Rig != null ? Main.Rig.GetComponent<Player_Health>() : null; }
            catch { return null; }
        }

        private static bool IsOwnPlayer(GameObject go)
        {
            if (Main.Rig == null) return false;
            try
            {
                var rm = go.GetComponentInParent<RigManager>();
                if (rm == null) return false;
                return rm.Pointer != null && rm.Pointer.IsEqualTo(Main.Rig.Pointer);
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

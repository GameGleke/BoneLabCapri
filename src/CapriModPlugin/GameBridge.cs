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
            // Руки ищем лениво в ReadControllerAxes (GetHandProp) — не при кэшировании рига.
        }

        /// <summary>Получить руку рига: сначала типизированное свойство (есть во всех версиях
        /// interop: RigManager.leftHand/rightHand), затем рефлексия по имени как fallback.</summary>
        private static Hand GetHandProp(RigManager rig, string propName)
        {
            try
            {
                // 1) типизированный доступ (компилируется против Il2CppSLZ.Marrow.dll вашей игры)
                Hand typed = propName == "leftHand" ? rig.leftHand : rig.rightHand;
                if (typed != null) return typed;
            }
            catch { }
            try
            {
                // 2) рефлексия по IL2CPP-типу
                var t = rig.GetIl2CppType();
                foreach (var m in t.GetMethods())
                {
                    if (!string.Equals(m.Name, "get_" + propName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (m.GetParameters().Length != 0) continue;
                    return m.Invoke(rig, null) as Hand;
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

            // руки обновляем лениво (на случай перевыбора аватара/сцены)
            if ((Main.RightHand == null || !Main.RightHand.gameObject.activeInHierarchy) && Main.Rig != null)
                Main.RightHand = GetHandProp(Main.Rig, "rightHand");
            if ((Main.LeftHand == null || !Main.LeftHand.gameObject.activeInHierarchy) && Main.Rig != null)
                Main.LeftHand = GetHandProp(Main.Rig, "leftHand");

            var hand = Main.RightHand != null ? Main.RightHand : Main.LeftHand;
            if (hand == null) return false;
            try
            {
                // Рефлексия через Il2Cpp-методы BaseController (публичные поля gripAxis/triggerAxis).
                // Прямые обращения к m_GripAxis/m_TriggerAxis ломают сборку на других версиях interop.
                var c = hand.Controller;
                if (c == null) return false;
                var t = c.GetIl2CppType();
                object g = GetIl2Member(c, t, "gripAxis") ?? GetIl2Member(c, t, "m_GripAxis");
                object tr = GetIl2Member(c, t, "triggerAxis") ?? GetIl2Member(c, t, "m_TriggerAxis");
                if (g is float gf && tr is float tf) { grip = gf; trigger = tf; return true; }
            }
            catch { }
            return false;
        }

        /// <summary>Чтение il2cpp-поля/свойства: сначала Mono-рефлексия по Il2Cpp-обёртке
        /// (interop генерирует свойства с теми же именами), затем IL2CPP FieldInfo/PropertyInfo.</summary>
        private static object GetIl2Member(Il2CppObjectBase obj, Type il2cppType, string name)
        {
            try
            {
                var monoField = obj.GetType().GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (monoField != null) return monoField.GetValue(obj);
                var monoProp = obj.GetType().GetProperty(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (monoProp != null) return monoProp.GetValue(obj, null);
            }
            catch { }
            try
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var fld = il2cppType.GetField(name, F);
                if (fld != null) return fld.GetValue(obj);
                foreach (var m in il2cppType.GetMethods(F))
                {
                    if (!string.Equals(m.Name, "get_" + name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (m.GetParameters().Length != 0) continue;
                    return m.Invoke(obj, null);
                }
            }
            catch { }
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
        /// <summary>Безопасный GetComponentInParent для IL2CPP-компонентов: у il2cpp-обёрток
        /// перегружен оператор == (null-check работает), но методы Unity могут кидать исключение
        /// вне игрового потока — всё в try/catch, при ошибке считаем что компонента нет.</summary>
        private static T SafeGetComponentInParent<T>(GameObject go) where T : class
        {
            if (go == null) return null;
            try
            {
                var comp = go.GetComponentInParent<T>();
                return comp != null ? comp : null;
            }
            catch { return null; }
        }

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
                if (SafeGetComponentInParent<AIBrain>(go) != null ||
                    SafeGetComponentInParent<NPCHealth>(go) != null ||
                    SafeGetComponentInParent<Player_Health>(go) != null)
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
            var npc = SafeGetComponentInParent<NPCHealth>(targetGO);
            if (npc != null) { kind = "npc"; return npc; }
            var ph = SafeGetComponentInParent<Player_Health>(targetGO);
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
                if (Main.Rig != null)
                {
                    var rigGO = Main.Rig.gameObject;
                    if (rigGO != null)
                    {
                        var ph = SafeGetComponentInParent<Player_Health>(rigGO);
                        if (ph != null) return ph;
                    }
                }
                var hp = FindHeadpoint();
                if (hp == null) return null;
                return SafeGetComponentInParent<Player_Health>(hp.gameObject);
            }
            catch { return null; }
        }

        private static bool IsOwnPlayer(GameObject go)
        {
            if (Main.Rig == null || go == null) return false;
            try
            {
                // RigManager — IL2CPP-объект: сравниваем напрямую (== перегружен interop'ом)
                var rm = SafeGetComponentInParent<RigManager>(go);
                return rm != null && rm == Main.Rig;
            }
            catch { return false; }
        }

        // ============ УНИВЕРСАЛЬНЫЙ ВЫЗОВ TAKEDAMAGE ============
        /// <summary>
        /// Нанести урон компоненту здоровья рефлексией — устойчиво к разным сигнатурам
        /// TAKEDAMAGE(float damage, Vector3 direction, DamageType type, ...) между версиями игры.
        /// healthComp — IL2CPP-обёртка: ищем метод по Il2Cpp-типу, вызываем m.Invoke(obj, args).
        /// </summary>
        public static bool TakeDamage(object healthComp, float amount, Vector3 direction)
        {
            if (healthComp == null) return false;
            try
            {
                Type type = (healthComp as Il2CppObjectBase)?.GetIl2CppType() ?? healthComp.GetType();
                foreach (var m in type.GetMethods())
                {
                    if (!string.Equals(m.Name, "TAKEDAMAGE", StringComparison.OrdinalIgnoreCase)) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 0) continue;
                    // первый параметр — float damage (в il2cpp-рефлекции типы могут приходить как Il2Cpp-обёртки)
                    var p0 = ps[0].ParameterType;
                    if (p0 != typeof(float) && p0.FullName != "System.Single") continue;

                    var args = new object[ps.Length];
                    args[0] = amount;
                    for (int i = 1; i < ps.Length; i++)
                    {
                        var pt = ps[i].ParameterType;
                        string pn = pt.FullName ?? "";
                        if (pt == typeof(float) || pn == "System.Single") args[i] = 0f;
                        else if (pt == typeof(bool) || pn == "System.Boolean") args[i] = true;
                        else if (pt == typeof(Vector3) || pn == "UnityEngine.Vector3") args[i] = direction;
                        else if (pt.IsEnum) args[i] = Activator.CreateInstance(pt);
                        else args[i] = null;
                    }
                    m.Invoke(healthComp, args);
                    return true;
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"GameBridge.TakeDamage: {e.Message}");
            }
            return false;
        }
    }
}

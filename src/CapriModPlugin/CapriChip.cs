using System;
using Il2CppSLZ.Bonelab;
using Il2CppSLZ.Marrow;
using MelonLoader;
using UnityEngine;

namespace CapriMod
{
    /// <summary>
    /// «CHIP OF CAPRI» — чип-конструкт. Игрок берёт чип руками и подносит к голове
    /// (как в аниме: чип вставляется в голову). Мы детектируем приближение к headpoint
    /// и «вставляем» чип — цепляем его к голове через JointModule игры. Пока надет:
    /// Капри активна, доступны телекинез и Scorch. Снял — Капри исчезает с репликой.
    /// </summary>
    public class CapriChip : MonoBehaviour
    {
        public static CapriChip Active { get; private set; }

        /// <summary>Все существующие чипы (обычно один).</summary>
        public static CapriChip[] AllChips = Array.Empty<CapriChip>();

        public bool IsEquipped { get; private set; }

        private const float EQUIP_DISTANCE = 0.18f;   // насколько близко к голове надо поднести
        private const float UNEQUIP_PULL   = 0.45f;   // на сколько оттянуть, чтобы вынуть

        private float _lastCheck;
        private int _equipCooldownFrames;

        private void OnEnable()
        {
            Active = this;
            RebuildAll();
        }

        private void Start() => RebuildAll();

        private static void RebuildAll()
        {
            var found = GameObject.FindObjectsOfType<CapriChip>();
            if (found.Length > 0) AllChips = found;
        }

        private bool _stickyMode;   // fallback: держим чип у головы kinematic-привязкой

        public void SetStickyMode(bool on) => _stickyMode = on;

        private void Update()
        {
            // Sticky-fallback: прижимаем позицию к голове каждый кадр
            if (_stickyMode && IsEquipped)
            {
                transform.position = GameBridge.HeadPosition + new Vector3(0.02f, 0.075f, 0.02f);
            }

            // Пассивная проверка раз в ~0.3 c (основной триггер — хук AttachObject)
            if (Time.time - _lastCheck < 0.3f) return;
            _lastCheck = Time.time;
            Evaluate();
        }

        /// <summary>Вызывается из Mono-потока после хука Hand.AttachObject.</summary>
        public static void CheckProximityToHead()
        {
            foreach (var chip in AllChips)
                if (chip != null) chip.Evaluate();
        }

        private void Evaluate()
        {
            if (_equipCooldownFrames > 0) { _equipCooldownFrames--; return; }
            if (GameBridge.FindHeadpoint() == null) return;

            float dist = Vector3.Distance(transform.position, GameBridge.HeadPosition);

            if (!IsEquipped && dist < EQUIP_DISTANCE)
            {
                Equip();
            }
            else if (IsEquipped && dist > UNEQUIP_PULL)
            {
                Unequip();
            }
        }

        private void Equip()
        {
            if (GameBridge.EquipChipToHead(gameObject))
            {
                IsEquipped = true;
                Active = this;
                MelonLogger.Msg("CapriMod: чип Капри установлен в голову.");
                CapriController.OnChipEquipped(this);
            }
        }

        private void Unequip()
        {
            IsEquipped = false;
            _stickyMode = false;
            GameBridge.UnequipChip(gameObject);
            // освобождаем от руки, чтобы чип упал физически
            try
            {
                var rb = GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = false;
                transform.position = GameBridge.HeadPosition + new Vector3(0.25f, -0.1f, 0.25f);
            }
            catch { }
            _equipCooldownFrames = 20; // не «вставлять» обратно сразу
            MelonLogger.Msg("CapriMod: чип Капри снят.");
            CapriController.OnChipRemoved();
        }

        /// <summary>Программное снятие (например, при смерти/выходе из сцены).</summary>
        public void ForceUnequip()
        {
            if (IsEquipped) Unequip();
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            bool wasEquipped = IsEquipped;
            IsEquipped = false;
            if (wasEquipped) CapriController.OnChipRemoved();
        }
    }
}
